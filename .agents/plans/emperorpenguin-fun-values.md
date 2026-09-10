# EmperorPenguin fun / async_fun / lambda 实现计划

日期：2026-08-31。分支：`feature/emperorpenguin-fun-values`（从 main 切出，待 `feature/lsp-project-config` 合并后进行）。

目标：让 EmperorPenguin（pass2/pass3）达到 BabyPenguin 的函数值能力——`fun<...>` / `async_fun<...>` 类型、lambda 表达式（含捕获）、函数/方法引用作为值、经值的间接调用——并让 `Tests/LambdaTest/*`、`Tests/AsyncTest/ImplicitCastForFunToAsyncFunTest.md` 在全部编译器上转绿。同时引入一个**两编译器同步实现的新语义：无绑定方法引用**（`A.b` 作为 `fun<A, P...>` 值，`h(a, x) ≡ A.b(a, x)`——`a.b(x) ≡ A.b(a, x)` 糖的值形式补全，BabyPenguin 作参考实现先行）。

## 现状（2026-08-31 实测，pass2/pass3 probe）

- **绿**：`LambdaTest/FunFieldMemberCall.md`（fun 类型字段持有全局函数引用 + `h.cb(21)` 字段调用）——该红哨兵已转绿，描述已过期（它声称"字段调用缺失"，实际字段调用已实现，缺失的是其它路径）。绑定在 `SemanticBindExpressions.penguin:2585-2602`，lowering 在 `IRGenerator.penguin:774-792`（RDMBR + CALL_INDIRECT）。`SemanticBindExpressions.penguin:3014-3017` 还有一条与之矛盾的过期注释（"EmperorPenguin does not support yet"），顺手清理。
- **红，全部死于 `E_INTERNAL: Function call has no callee symbol`**（局部 fun 变量/lambda 的调用走到 `SemanticBindExpressions.penguin:3034` 的 void 兜底）：`LambdaBasicTest`、`LambdaBasicReturnTest`、`FunctionVariableTest`（`let y: fun<void> = x; y();`）、`FunctionBindingTest`（`x.call` 绑定方法）、`StaticFunctionBindingTest`、`AsyncFunctionBindingTest`、`AsyncFunctionVariableTest`。
- **红（诊断不符）**：`WrongFunctionTypeTest` —— EP 编译失败但没有 `E_TYPE_MISMATCH`（fun 类型赋值不兼容未检查）。

### 已有脚手架（比预期多）

| 层 | 已有 | 缺口 |
|---|---|---|
| 词法/语法 | lambda 表达式完整解析（`Parser.penguin:70-71, 2602-2624`，含 `fun { ... }` 无参简写与 `async_fun` lambda）；`fun<...>` 类型（`Parser.penguin:1074-1083`） | `async_fun<...>` 类型位置不识别（`Async_fun` token 存在但 `parse_typeSpecifier` 不认）；`parse_typeSpecifierInGeneric`(:1168)、`try_parse_genericTypeArgs`(:1125)、`is_type_name_token`(:360) 不认 `Fun`/`Async_fun` → `Option<fun<...>>` 这类嵌套解析丢失 |
| AST | `LambdaFunctionExpression`（`AST.penguin:390-413`）、`TypeSpecifier.is_function_type/is_async_function_type/function_params`（:989-1009）、`FunctionDefinition.is_async/is_not_async` | — |
| bound | `TypeKind.FunctionKind` + `is_async_function`（`BoundType.penguin:31,460`，display `fun<...>`/`async_fun<...>` :470-483，`is_same_type` 比较 async 标志 :565）；`make_function_type(ret, params, is_async)`（`BoundTypeRegistry.penguin:248-261`，**generic_args[0]=返回值**，其余为参数——BabyPenguin 约定）；`resolve_type_specifier` 已处理 fun 类型（`SemanticModel.penguin:684-704`）；`BoundLambdaExpression` 节点存在（`BoundExpression.penguin:262-280`） | **`bind_expression` 无 `Expression.lambda_expr` case**（`SemanticBindExpressions.penguin:21-99` 落到 none()，lambda 静默消失）；标识符 callee 路径（:2508-2572）只认 `function_sym`，不认 fun 类型变量；方法引用 `x.m` 值位置不产 fun 类型（this 参数不剥离）；`can_implicitly_cast`（`BoundTypeRegistry.penguin:149`）无 fun→async_fun 规则 |
| IR/LLVM | `FunctionKind → "funptr"`（`IRGenerator.penguin:1992`）；函数名作值 → funptr 常量（`IRGenerator.penguin:494-504`）；`CALL_INDIRECT` 指令 + 发射（`IRBuilder.penguin:155-164`、`LLVMEmitter.penguin:3620-3655`，已处理 sret/聚合返回）；funptr 常量直接映射 `@symbol`（`LLVMEmitter.penguin:2698-2711`） | funptr 是**裸代码指针，无 env/owner 词**——闭包与绑定方法放不进去 |
| async | `is_async` 解析并存进 `BoundFunctionSymbol`（`SemanticResolveTypes.penguin:175-177`），但 IR/LLVM **零消费**；`async f(args)` 走 `bind_spawn_async` 的 bind 期类合成（`SemanticBindExpressions.penguin:839-1245`）；栈式协程 C 运行时（`std/c/scheduler.c`）；`--enable-coroutine` 门（`CompilerConfig.penguin:62-70`） | `async_fun<...>` 类型语法；async 函数符号的类型不带 async 标志的核实 |

**BabyPenguin 对照实现**（语义基准）：
- lambda → 闭包类脱糖（`BabyPenguin/SemanticPass/06_SyntaxRewriting.cs:123-170` + `SemanticInterface/ITypeContainer.cs:58-129`：捕获字段 + ctor + `call` 方法，体内捕获标识符重写为 `this.<name>`），lambda 表达式重写为 `(new Closure(captures)).call`
- 函数值 = 胖指针 `{FunctionSymbol, Owner}`（`VirtualMachine/RuntimeValue.cs:179-223`）；方法引用类型 = 剥离 this 参数后的 fun 类型（`SemanticInterface/ICodeContainer.cs:1884-1889`）
- 经值调用：owner 非空且非 static 时把 owner 前插为第一实参（`VirtualMachine/RuntimeFrame.cs:612-688`）
- async：`async_fun` 与 `fun` 同为 `TypeEnum.Fun`，FullName 相同仅差 `IsAsyncFunction` 标志 → fun 隐式转 async_fun（`Type/BasicTypes.cs:40-47` 结构相等）；直接调用 async 值 = 内联执行 + 隐式 wait 改写（`06_SyntaxRewriting.cs:204-243`）；仅 `async <call>` 才 spawn
- 相等性：比较 FunctionSymbol（`ExternFunctions.cs:836-837`）

## 核心设计决策：fun 值 = callable 对象（单 ptr、GC 管理、vtable 槽 0）

**EmperorPenguin 的 fun 值是一个对象指针**，指向一个 GC 堆上（或常量全局）的对象，其类元数据的 interface_map 携带 `.__FunVal` 条目，槽 0 指向一个**以对象自身为第一参数**的方法（与 `mut this` 方法 ABI 相同）。调用点统一编译为：

```llvm
%fn = call ptr @_emperor_vtable_lookup(ptr %funval, ptr @.__FunVal_interface_id, i32 0)
%r  = call <ret> %fn(ptr %funval, <args...>)
```

四种 fun 值来源共用这一表示：
1. **顶层/静态函数引用**（`let f: fun<i32,i32> = twice;`）：每函数合成 thunk（丢弃第 0 参数、转发原函数）+ 常量单例对象 `@__funval_<fn>_obj = private constant { ptr @metadata }`（无字段类实例 = 仅元数据指针一个词，纯常量、零运行时分配）；现有 funptr 常量路径（`LLVMEmitter.penguin:2698-2711`）改映射到该单例
2. **绑定方法引用**（`let func: fun<i32,i32> = x.call;`）：每 (类,方法) 首次引用时合成 invoker 类 `{ __recv 字段; fun __call(mut this, args...) { return this.__recv.m(args...); } }`，`new` 出的对象即 fun 值
3. **无绑定方法引用**（`let h: fun<Temp, i32, i32> = ns.Temp.call;`，新语义，BabyPenguin 同步实现）：fun 类型**保留** this 参数（`fun<A, P...>`）；常量单例（同 1 的机制），thunk 不丢弃第 0 真实参数、原样作为 receiver 转发（`@__funval_thunk_A_b(ptr %self, ptr %a, ...) → A.b(%a, ...)`）
4. **lambda**：闭包类（捕获字段 + `fun __call(mut this, params) { 体重写 }`），闭包对象本身就是 fun 值（不再包 invoker，避免双重间接）

**语义等式**（贯穿设计，两编译器统一）：`a.b(x) ≡ A.b(a, x)`（直接调用今天已成立）；绑定引用 `g = a.b` 后 `g(x) ≡ A.b(a, x)`（receiver 取引用时固化）；无绑定引用 `h = A.b` 后 `h(a, x) ≡ A.b(a, x)`（receiver 由调用方作第一实参传入）。糖在语言可观察行为层面成立，不在 ABI 位级成立（无法从 fun 值反取函数地址/receiver，无运行时绑定/解绑定操作——BabyPenguin 同）。接口方法无绑定引用（`I.b`）报错：接口方法没有唯一实现。

**为什么不是 BabyPenguin 式双字胖指针 `{fn, env}` 按值传递**：
- GC 安全：类元数据的 `field_is_ptr` 按字段粒度标记，无法表达内联双字结构中"第二个词是指针"；fun 值作字段 / `Option<fun<...>>` payload 的可达性追踪会漏。单 ptr 表示下 fun 字段就是普通指针字段，现有标记机制直接工作（`FunctionKind.is_value_type()` 已返回 false → 引用语义，与 BabyPenguin `ToType(Mutability.Mutable)` 一致）
- 调用点无法静态判断被引函数是否接收 this/env；统一"第 0 参数 = 对象自身"ABI 后无需为每个函数生成 trampoline（仅静态引用需要，且是确定性命名 → 相等性保持）

**已知行为差异**（记录，不阻塞）：`==` 为指针相等——静态引用同一单例 → 相等（匹配 BabyPenguin）；无绑定引用 `A.b == A.b` 也相等（EP 同一单例 / BabyPenguin 同一 FunctionSymbol）；绑定方法每次取引用生成新 invoker → `x.m == x.m` 为 false（BabyPenguin 为 true），后续可用 invoker 缓存修正。性能：每次间接调用一次 vtable strcmp 扫描（与接口调用同款）；后续优化可将 thunk 存进 `EmperorClassMetadata` 当前恒为 null 的 `virtual_method_table` 槽位直达。

**`__FunVal` 条目的实现方式**：不引入 PenguinLang 可见的接口定义（模板定长参数无法表达任意签名的方法族）。`BoundClassDefinition` 加 `is_funval: bool` 标记，由 lambda/invoker/静态单例的合成方设置；`LLVMEmitter.emit_full_class_metadata`（:1969-2060）对标记类在 interface_map 追加 `{ @.__FunVal_interface_id, [ @<cls>__.__call 或 @thunk ] }`（interface_id 沿用 `.<name>_interface_id` 点前缀形状，`interface_map_name` :4170-4178 截断 `<` 对固定 id 无影响）。

## 里程碑

每里程碑独立提交、保持绿色；里程碑收尾跑 `make bootstrap`（需 pass5 md5 收敛）+ `dotnet run --project Tests -- --filter 'LambdaTest/*' --compilers babypenguin,pass2,pass3 --probe`。

### 里程碑 0：分支 + 红哨兵铺底

- 从 main 切 `feature/emperorpenguin-fun-values`（LSP 分支合并后）
- 更新 `FunFieldMemberCall.md` 描述（字段路径已实现、已转绿；改述为锁定该路径的回归测试），清理 `SemanticBindExpressions.penguin:3014-3017` 过期注释
- 按 AGENTS.md 红哨兵规范，把这些 BabyPenguin-only 测试的 Apply To 扩到 `EmperorPenguin Pass2, Pass3`（描述注明"should turn green once implemented"）：`LambdaBasicTest`、`LambdaBasicReturnTest`、`FunctionVariableTest`、`FunctionBindingTest`、`StaticFunctionBindingTest`、`AsyncFunctionBindingTest`、`AsyncFunctionVariableTest`、`WrongFunctionTypeTest`、`AsyncTest/ImplicitCastForFunToAsyncFunTest`（后两个 async 的在 M5 前保持红）
- 新增 `Tests/LambdaTest/UnboundMethodBindingTest.md`（`let h: fun<Temp, i32, i32> = ns.Temp.call; h(x, 2)` → `3`，即在 `FunctionBindingTest` 的类上取无绑定引用、显式传 receiver 调用）——**双红哨兵**（BabyPenguin 与 EmperorPenguin 都还没有该语义），Apply To: BabyPenguin + Pass2/Pass3，随里程碑 3 转绿

### 里程碑 1：语法补全（async_fun 类型 + 嵌套位置）

- `Parser.parse_typeSpecifier`（:1074-1083）加 `TokenType.Async_fun` 分支 → `is_async_function_type = true`
- `parse_typeSpecifierInGeneric`（:1168）、`try_parse_genericTypeArgs`（:1125）、`is_type_name_token`（:360）接受 `Fun`/`Async_fun` → `Option<fun<void>>`、`List<fun<i32,i32>>` 可解析
- `EmperorPenguin.Tests` 补 AST build-text 用例（`ASTBuildTextTest` 风格：`async_fun<i64, string>` 嵌套在泛型参数内）
- 新增编译通过测试 `Tests/LambdaTest/NestedFunType.md`（`Option<fun<void>>` 字段/局部，Apply To: BabyPenguin + Pass2/Pass3）
- 快速回路：本里程碑起可用 `--compilers pass1` 迭代（pass1 经 BabyPenguin 即时编译 EP 源码，免 bootstrap）

### 里程碑 2：fun 值调用核心（局部/参数 + 静态引用 + 调用 ABI）

- **绑定**：`bind_function_call` 标识符路径（`SemanticBindExpressions.penguin:2508`，`function_sym` 分支之后）加 `sym is BoundSymbol.variable && bound_type.kind is FunctionKind` 分支——镜像字段分支 :2585-2602 的返回类型推导（`generic_args[0]`），产出 `callee = identifier`、`callee_symbol = 变量符号` 的 `BoundFunctionCallExpression`；实参按 fun 类型其余泛型参数 `check_argument_types`。这同时覆盖局部变量、函数参数（higher-order）、类字段经标识符的形态
- **lowering**：`IRGenerator` 调用路径加对应分支——`get_symbol_reg`（fun 值寄存器；全局 fun 变量先 `global_load`）+ `emit_call_indirect`；字段路径 :774-792 结构不变（RDMBR 加载的即对象指针）
- **ABI 落地（LLVMEmitter）**：
  - funptr 常量（:2698-2711）改映射到 `@__funval_<fn>` 单例：惰性合成 thunk 函数（`define <ret> @__funval_thunk_<fn>(ptr %env, <params...>)` 转发 `@<fn>`）+ 该 thunk 的独立元数据（interface_map 含 `.__FunVal` 条目）+ 常量单例全局；不可变寄存器 reg_map 直映射与可变寄存器 alloca store 两条路径都覆盖
  - `emit_call_indirect`（:3620-3655）语义变更：操作数从代码指针改为 callable 对象 → `_emperor_vtable_lookup(obj, ".__FunVal", 0)` 取 thunk，并把 obj 前插为第 0 实参；sret/聚合返回逻辑保持
- **类型检查**：`can_implicitly_cast`（`BoundTypeRegistry.penguin:149`）加规则——两边均 FunctionKind、签名相同（`is_same_type` 忽略 async 标志的参数级比较）且 `from 非 async → to async` 允许（`fun<i32>` → `async_fun<i32>`；反向与 async→fun 拒绝）；赋值/声明不兼容时报 `E_TYPE_MISMATCH`
- 测试转绿：`FunctionVariableTest`、`WrongFunctionTypeTest`、`FunFieldMemberCall`（保持）；新增 `Tests/LambdaTest/HigherOrderFunTest.md`（fun 作函数参数传递并回调）+ `Tests/LambdaTest/FunIndirectAggregateReturn.md`（间接调用返回 string，锁 sret 路径）

### 里程碑 3：方法引用（BabyPenguin 无绑定参考实现 + EmperorPenguin 绑定/静态/无绑定）

**BabyPenguin 侧（先行——参考编译器定语义，新语义 = 无绑定方法引用）**：

- `BabyPenguin/SemanticInterface/ICodeContainer.cs` 成员访问绑定：base 为**类型符号**、成员为实例方法、处于**值位置**时加无绑定分支——fun 类型**保留** this 参数（对照绑定分支 :1884-1889 剥离 `Skip(2)`，无绑定不剥离，类型为 `fun<Ret, A, P...>`），`FunctionRuntimeValue` 不设 Owner
- 运行时**无需改动**：`IRCallFuncPtrInst`（`VirtualMachine/RuntimeFrame.cs:612-688`）Owner 为空时本就不前插、实参按位透传——receiver 由调用方作第一实参显式传入，类型检查由 fun 类型的第二个泛型参数（A）承接
- C# 后端 `BabyPenguin/CSharpBackend/FunctionLowerer.cs` 的 funptr 溯源补无绑定形态（无 receiver 前插、原样调用）
- 约束（两个编译器一致）：接口方法 `I.b` 无绑定引用报错（无唯一实现）；泛型方法需引用点显式特化（`A.m<i32>`）
- 验证：`UnboundMethodBindingTest` 在 BabyPenguin 转绿

**EmperorPenguin 侧（绑定 + 静态 + 无绑定）**：

- **类型**：`bind_member_access` 的方法成员在**值位置**产出 fun 类型——绑定时 `make_function_type(ret, parameters[1..], sym.is_async)`（剥离 this，对照 `ICodeContainer.cs:1884-1889`）；无绑定（base 为类型符号）时 `make_function_type(ret, 全参数含 this, sym.is_async)`。调用位置不受影响：`bind_function_call` 先 bind callee，其 `:2604` 分支按 `member_symbol is function_sym` 走直呼，与 member_access 的 bound_type 无关（实施时核对该顺序，避免 `x.m(args)` 误入间接路径）
- **invoker 合成**：`new` 一批 per-(类,方法) invoker 类（字段 `__recv` + `fun __call(mut this, ...) { return this.__recv.m(...); }`），**非泛型、具体类型拼写**（复用 `spawn_type_spec` 从 BoundType 构造 TypeSpecifier；合成+`catch_up_def_before_bodies` 完全照抄 `bind_spawn_async` :928-1032, :1146-1174 模式），`SemanticModel` 上按 `类全名#方法名#特化` 缓存防重复；泛型类方法按 receiver 的具体特化合成（名字含特化后缀）
- **lowering**：`IRGenerator` 的 member_access 分支对 fun 类型且 symbol 为 function_sym 的值位置求值 → 发射 `new <invoker>`（receiver 求值一次存入 `__recv`）
- **静态方法引用**（无 this 的方法，`StaticFunctionBindingTest`）：直接走里程碑 2 的 `@__funval_<fn>` 单例（thunk 转发静态方法）
- **无绑定方法引用**（`A.b` 值位置 → `fun<A, P...>`）：单例机制同静态引用，差别仅在 thunk **透传**第 0 真实参数作 receiver（`__funval_thunk_A_b(ptr %self, ptr %a, ...) → A.b(%a, ...)`），thunk 命名含类与方法以区分静态函数 thunk
- 测试转绿：`FunctionBindingTest`、`StaticFunctionBindingTest`、`UnboundMethodBindingTest`（EP 侧随之转绿）

### 里程碑 4：lambda（捕获闭包）

- `bind_expression` 加 `Expression.lambda_expr` case（`SemanticBindExpressions.penguin:21-99`），在**当前函数体绑定现场**（scope 链可用）执行：
  1. 参数/返回类型 `resolve_type_specifier`（无 `->` 默认 void）
  2. **捕获分析**：遍历 lambda 体 AST 的 Identifier，对当前 scope 链 `lookup_symbol`；命中外层函数的 param/local（排除 lambda 体内新声明、排除类型/命名空间符号）→ 按出现顺序去重成捕获列表
  3. **合成闭包类**（BabyPenguin `AddLambdaClass` 形状）：每捕获一字段（类型 = 捕获变量的 BoundType 经 `spawn_type_spec`）+ `fun new(mut this, 捕获...)` + `fun __call(mut this, 参数...) -> ret`（体内捕获标识符 AST 重写为 `this.<name>` 成员访问）；非泛型；`source_file = "<lambda>"`；类名 `__lambda_<n>`（model 计数器）；置 `is_funval = true`
  4. 注册进 unit + `catch_up_def_before_bodies`
  5. 表达式绑定为 `new __lambda_N(cap0, ...)`，**bound_type 直接置 fun 类型**（闭包对象即 fun 值；`__FunVal` 槽指向 `__call` 本体）
- `BoundClassDefinition.is_funval: bool` 字段 + `emit_full_class_metadata`（:1969-2060）对标记类追加 `__FunVal` interface_map 条目（若 M2/M3 已用该机制则此处只接线）
- 捕获语义：**按值快照**（构造时拷贝进字段），`mut` 局部的捕获是副本——文档化；`this` 捕获 v1 不支持（报清晰错误）
- async lambda（`async_fun {...}`）：fun 类型带 async 标志；体内 `wait` 受 `--enable-coroutine` 门（现状）
- 测试转绿：`LambdaBasicTest`、`LambdaBasicReturnTest`；新增 `Tests/LambdaTest/LambdaCaptureTest.md`（捕获局部+参数并调用）、`Tests/LambdaTest/LambdaCaptureSnapshot.md`（快照语义：捕获后修改原变量不影响闭包）——先在 BabyPenguin 验证基准行为再定 EP 断言

### 里程碑 5：async_fun 值

- **is_async 流向**：核对所有 `make_function_type` 调用点带上符号的 `is_async`（`SemanticResolveTypes.penguin:175-177` 已带、:219 等处传常量 false 的逐一核实）；方法引用类型带方法的 is_async（M3 的剥离逻辑已留参）
- **调用语义（栈式协程）**：`async_fun` 值直接调用 = 当前协程栈内联执行，被调内部 `wait` 挂起整个栈——这**就是** BabyPenguin 隐式 wait 的语义，EP 无需任何改写（`AsyncFunctionVariableTest` 的 `let z: i32 = y();` 应自然工作）；`async fun` 声明的函数体内含 `wait` 时要求 `--enable-coroutine`（沿用 `require_coroutine`）
- **fun→async_fun 隐式转换**：M2 已加规则，此处验证 `ImplicitCastForFunToAsyncFunTest` 转绿
- **`async fval(args)`（经 fun 值 spawn）**：`bind_spawn_async` 目前要求直接调用（:846-859）——扩展为间接被调者也接受：合成 ctx 的 `__enter` 里对 fun 值发间接调用。**可选延后项**，不阻塞本里程碑测试
- 测试转绿：`AsyncFunctionBindingTest`、`AsyncFunctionVariableTest`、`ImplicitCastForFunToAsyncFunTest`

### 里程碑 6：收尾

- 文档：`EmperorPenguin/README.md`（fun 值表示与 `__FunVal` 元数据条目）、`Documentation/`（函数类型/lambda 章节，含语义等式 `a.b(x) ≡ A.b(a, x)` 的值形式说明与无绑定引用，若已有则更新）；**技能表更新**：`penguinang-coding` 的"Lambda / 函数值在编译器源中禁用"条目——EP pass2+ 支持后该限制的原始理由（EP 编译不了自身源码中的 lambda）消失，可解除（bootstrap 链 pass1 用 BabyPenguin 编译本就支持）；保守起见标注"新解除，编译器源码暂不主动使用"
- `.agents/memory/` 记录（ABI 决策、已知差异、优化路径）
- 全量验证（tee 到 /tmp/test.log）：`make bootstrap`（pass5 收敛）→ `make test` 全矩阵 → `dotnet test` → `make lsp` 确认不受影响 → （linux host）`make lsp_win` 交叉构建抽查
- 用 `--probe` 扫一遍其余类别（GenericTest/InterfaceTest 等）确认无回归

## 风险与对策

- **GC 追踪**：fun 字段、`Option<fun>` payload、闭包持有引用捕获的可达性——`FunctionKind` 已归引用类型（`is_value_type` 返回 false），字段按 ptr 标记；写一个"fun 值存进 Option/List 字段 + 大量分配触发 GC"的压力测试防漏标
- **合成类的单态化可见性**：闭包/invoker 类必须非泛型、具体类型拼写（`bind_spawn_async` :1034-1036 注释的教训——pass3 fixpoint 看不见按需泛型实例化）
- **funptr 常量双路径**：reg_map 直映射（`LLVMEmitter.penguin:2707`）与可变寄存器 alloca store（:2703-2705）都要指向单例对象；`sanitize_name`/`llvm_func_name` 对 `__funval_*` 命名的确定性保证相等性稳定
- **interface_id 碰撞**：`.__FunVal` 固定 id 无泛型后缀；点前缀与现有 interface_id 形状一致，无用户可见命名冲突
- **调用/值位置歧义**（M3 最大坑）：`x.m(args)` 直呼 vs `x.m` 取引用——绑定顺序上 `bind_function_call` 先 bind callee 再按 `member_symbol` 分派，理论隔离；实施时对 `x.m` 泛型方法引用（需显式 `x.m<i32>` 取特化）明确报错或支持，写测试锁定
- **BabyPenguin 行为差异**：绑定方法 `==` 指针相等性（见设计决策）；`cast<string>(fun值)` BabyPenguin 打印函数名——EP v1 报清晰 `E_UNSUPPORTED` 而非崩
- **无绑定引用的边界**：接口方法 `I.b`（无唯一实现）与泛型方法未显式特化（`A.m` 不带 `<i32>`）在两个编译器上一致报错；从 fun 值反取函数地址/receiver、运行时绑定/解绑定两编译器均不支持——Documentation 明确记录
- **BabyPenguin 侧回归面**：`ICodeContainer.cs` 成员访问分支与 C# 后端 funptr 溯源改动可能波及既有绑定/静态引用路径——`dotnet test`（BabyPenguin.Tests）+ `FunctionBindingTest`/`StaticFunctionBindingTest` 既有用例必须保持绿
- **动态库边界**：`.penguin-lib` 跨界传递 fun 值（嵌入源码本地单态化 vs `.so` 内定义的符号可见性）超范围，README 记录限制
- **bootstrap 自举**：实现代码本身必须留在 ANTLR 安全子集（不使用 lambda/fun 类型/嵌套命名空间）——鸡生蛋约束，纯数据结构与分支代码无压力；每里程碑 `make bootstrap` 保证 pass5 md5 收敛

## 验证命令（各里程碑通用）

```bash
# 快速回路（EP 语义改动即时生效，免 bootstrap）
dotnet run --project Tests -- --filter 'LambdaTest/*' --compilers pass1 --probe --compare-with none
# 里程碑收尾
make bootstrap && dotnet run --project Tests -- --filter 'LambdaTest/*' --compilers babypenguin,pass2,pass3 --probe | tee /tmp/test.log
# 进程内单测（AST/bound 改动配套用例）
dotnet test EmperorPenguin.Tests | tee /tmp/test.log
```
