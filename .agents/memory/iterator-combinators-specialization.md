# 泛型特化三坑 + 组合子/方法模板修复记录（T6 及后续）

分支 `feature/emperorpenguin-fun-values`。测试锚点：`Tests/BuiltinTest/IteratorCombinators*.md`、`Tests/GenericTest/MethodTemplateQualifiedTypeArg.md`、`Tests/EmitterTest/UserVarTempNameCollision.md`。

## 三坑（T6 迭代器组合子开发中踩定，所有 pass-8 按需特化都必须遵守）

1. **特化守卫协议**：pass-3 AST 收集器绝不能把"解析失败的类型参数"（`?`/dummy）喂给
   `ensure_specialized_def` — dummy 特化名内嵌上级 mangle 名，下轮再收集再特化 → 指数增长
   （曾 277MB 日志 OOM）。守卫 = `bound_type_is_template_param`（dummy 特征：type_symbol/scope
   为 none + 成员全空的 BoundClassDefinition），挡在 ensure_specialized_def 入口 / run() 函数
   实例化循环 / bind_new_expr / try_infer_generic_call / resolve_new_generic_args_from_ast 五处；
   配合 `SemanticModel.suppress_report_errors` 只静默 pass-3 收集窗（pass-8 正常报错）。
2. **裸名 AST 歧义**：`find_ast_def_by_name` 反向裸名搜索会把 `RangeIterator.map` 解析成
   `FilterIterator.map`（第一个同名 def）。`BoundFunctionDefinition.ast_source`（mut 字段，
   `bind_function_def` 设置、`specialize_function_def` 复制）持有真正的源 def —— 特化 /
   ensure / replay 一律优先 ast_source。
3. **推断 ABI（pass-8 按需特化的发射前提）**：pass-8 创建的特化必须走
   resolve-at-3 → this-fix → `unit.definitions.push` 注册 → `catch_up_def_before_bodies_as_pass8`
   完整协议，否则 IR 层无 layout、所有槽位默认 i64（i64 元素因 stdlib 早有实例侥幸，i32 必挂）。
   参照 `gen_ensure_builtin_spec` / `ensure_function_specialization_now`。

另两个 T6 定型教训：单个 AST 节点实例禁止共享进多个父节点（就地污染，每个使用点新建）；
pass-3 fixpoint 遇**歧义方法模板**（多个类定义同名 map/filter/into）必须跳过，交给带接收者
推断的 pass-8 ensure 路径 —— 裸名挂错类会产出垃圾体。

## lower_new 的类型参数基名（MethodTemplateQualifiedTypeArg / IntoVector 修复）

方法级 `#template(C: type)` 体里的 `new C()`，特化后 `type_symbol` 仍是**函数作用域类型参数
符号**（full_name 是 `...into$X.<body>.C` 作用域路径，不是类名）→ 旧代码 mangle 出不存在的
`C$<hash>` 类 → `find_class_layout` MISS → `emit_new` **静默跳过**（"; NEW ... (no layout)"）
→ 结果寄存器未定义 → 链接期 `use of undefined value '%t1'`。

修复（IRGenerator.lower_new）：`bound_type.type_definition` 有值时，generic_args 分支用模板
def 的 full_name 作 mangle 基（prefix 从其**最后一个** '.' 取，比旧 splice 的第一个匹配更稳）；
无 generic_args 分支（C 落到具体类或 M4 特化 def）直接用 def 的 full_name。fun 类型（bt_is_fun）
不动。当初预想还要修"按需特化类不发构造器"，实际不需要 —— ensure 路径的 catch_up 含 pass-5，
构造器正常发射（FilterIterator$<hash>_new 一直在 .ll 里）。

诊断技巧：`grep "no layout" combined.ll` 一发命中被静默跳过的 NEW —— **emit_new 的静默跳过会
把发射期错误推迟成链接期 undefined，任何 no layout 行都是 bug**。

## fun 值间接调用的值类型参数 ABI（MetaJson/TrailingBool pass3 链接红修复）

症状：`%t7 = call i8 %tmp_702(ptr %pred, ptr %tmp_703)`，`'%tmp_703' defined with type
'%class.X' but expected 'ptr'`。触发面很宽：只要 vector.penguin 等被加载，其组合子方法
（`all`/`filter` 体里的 `pred(v)`）被特化发射，即使**没人调用**，含值类型参数的 fun 值调用
就产生非法 IR → 链接失败（这也是"测试没用组合子却红"的原因）。

根因：fun 值调用（`emit_call_indirect`）用的是裸 `emit_args` —— 值类型参数（IR `ref<Item>` →
LLVM `ptr`）实参却 resolve 成 struct **值**（`load %class.Item`），产出 `ptr %struct值`。
桩（闭包 `__call` / ensure_funval thunk / bound forwarder）的参数声明与其他函数一致（emit_function
规则：ref<X>→ptr ABI、>16B enum→byval），调用方必须对齐。

修复（LLVMEmitter）：`emit_call_indirect` 改用 `emit_args_virt`；同时给 `emit_args_virt` 的 else
分支补上 `struct_value_to_ptr`（实参注册为 struct 值、参数类型 ptr → 传存储地址），与
emit_args_coerced / emit_args_with_first 对齐 —— 接口虚调用 + 值类 struct 值实参的组合此前
也潜伏着同样的洞。

## BP wait <int> 短定时器 vs 端口（端口簇 12 红修复）

`wait this.x`（x 为 i64 输入端口）：`ResolveExpressionType(this.x)` 是 int → 落进
`wait <n>` 短定时器分支（06_SyntaxRewriting.RewriteWaitExpression）→ 合成
`__builtin._after(cast<i64>(this.x))`，annoymous 源码里 `this.x` 再解析时丢 owner →
"Cannot read input port 'x' of another module"（在模块**自己**的 initial 里！）。
修复：int 分支除 `operandIsCall` 外再排除端口成员访问（`ResolveMemberAccessExpressionSymbol` +
`PortRegistry.Find` 命中即跳过，走 ICodeContainer 的事务 wait 分支）。
经验：`annoymous:1:<col>` 的错误列号可直接定位到合成源码串的偏移（26 = `__builtin._after(cast<i64>(`）。
