# fun / async_fun / lambda 值 — ABI 与实现记录

分支 `feature/emperorpenguin-fun-values`（M0-M6，2026-09-11 完成）。计划：`.agents/plans/emperorpenguin-fun-values.md`。测试：`Tests/LambdaTest/`（20 个）+ `Tests/AsyncTest/ImplicitCastForFunToAsyncFunTest.md`。

## ABI：callable 对象（单 ptr + `__FunVal` 元数据条目槽 0）

- 函数值 = 对象指针；元数据 interface_map 有 `.__FunVal` 条目，槽 0 = 以对象自身为第 0 参数的函数（`mut this` ABI）。
- 间接调用：`_emperor_vtable_lookup(obj, ".__FunVal", 0)` → `call ret %fn(ptr obj, args...)`。
- 四种来源：静态/顶层引用（常量单例 + 转发 thunk）、绑定方法（`{meta, recv}` 16 字节堆对象 + 前向器从 offset 8 载 receiver）、无绑定方法（常量单例 + thunk 透传第 0 参作 receiver；fun 类型 `fun<A, P...>` 的 A 是第一个参数）、lambda（闭包对象本身就是 fun 值，`is_funval` 类标记 → `ensure_class_layout` 追加 `__FunVal` 条目指向 `__call`）。
- 为什么不是 BabyPenguin 式 `{fn, env}` 胖指针：GC 的 `field_is_ptr`/refmap 按字段粒度，无法表达内联双字结构的第二个词是指针 → fun 字段/Option payload 会漏标。单 ptr 表示下现有机制直接工作。
- 类型约定（两编译器一致）：`fun<R, P1...>` **generic_args[0] = 返回类型**，其余为参数；无绑定引用保留 this 参数（receiver = 第一个参数）。

## lambda 脱糖（EP，bind 期）

`bind_lambda`（SemanticBindExpressions.penguin）：
1. 外层作用域解析参数/返回类型（lambda 局部类型名在闭包类作用域不可见）。
2. 捕获分析：遍历 lambda 体 AST，scope 链逐层找 variable 符号 — Function/Block/InitialRoutine 域 = 可捕获；Class/Enum/Interface/Impl 域 = 清晰报错（v1 不支持）；Namespace/Global = 全局（__call 内自然解析，非捕获）。`this` 捕获 → E_UNSUPPORTED。
3. shadow 追踪（块级词法 + 顺序 let 作用域 + for/catch/try-bind 变量），标识符原位重写为 `this.<name>`。**漏遍历的表达式变体是响亮失败**（__call 绑定时 E_UNDEFINED），不会静默错。
4. 合成 `__lambda_<n>`（计数器 `SemanticModel.lambda_site_counter`）：捕获字段（按值快照、保留源可变性）+ 显式构造器 + `__call(mut this, params)`；注册走 spawn 模式（bind_definition → resolve_pair@pass2 → catch_up_def_before_bodies → unit.definitions.push）。
5. 表达式绑定 = `new __lambda_N(<captures>)` **重定型为 fun 类型**（新建 BoundNewExpression — 旧节点字段不可变，不能原地改 bound_type）。

**funval 类恒为引用类型**（`classify_class` 前置分支）— 否则全值类型字段的闭包会被分类为值类型 → 栈分配 → 逃逸后悬垂（LambdaCaptureTest 抓过这个）。

## 关键坑（踩过的）

1. **EP 源码自举可变性**：`let x: T` 局部不可重赋值；不可变绑定的值不可赋给 `mut` 槽（`target = tk` E_MUTABILITY）— 用索引/标记位追踪（bind_binary），或新建节点（BoundNewExpression 重定型），或 `new Option.some(field.some)` 包一层（scope 链游走）。
2. **`lower_new` 与 fun 类型**：闭包 NEW 的 bound_type 是 fun 类型，其 generic_args（ret+params）不能喂给类特化 mangle（产生不存在的类名 → NEW "no layout" → 寄存器无定义）— FunctionKind 守卫。
3. **前向器/thunk 必须真的定义 %recv**（从对象字段加载），不是引用。
4. **println 两编译器都输出尾部换行**；测试期望块要含尾 `\n`（对照 HigherOrderFunTest）。
5. 测试 runner 用 BabyPenguin/bin/Release — 调试须 `dotnet build -c Release`。
6. async 测试需要 `Compile.Args: --enable-coroutine`（BabyPenguin 忽略 args，EP 必需）。

## 已知行为差异（vs BabyPenguin）

- `==`：EP 指针相等；绑定方法 `x.m == x.m` 为 false（BP 比较函数符号为 true）— 可用 invoker 缓存修正。
- fun↔async_fun：**双向互通**（BP FullName 相等两向都允许；EP `can_implicitly_cast` 对称规则 `is_async_function !=` + `function_signatures_equal`）。
- 嵌套 lambda 引用更外层函数局部量：BP 逐闭包解析支持；EP v1 响亮失败（外层重写 this.x 在内层 __call 不解析）。文档化限制。
- `cast<string>(fun值)`：BP 打印函数名；EP v1 未定义（勿用）。
- 跨 `.penguin-lib` 边界传函数值：**已支持**（T3 验证：consumer lambda 进 lib、lib lambda 出、lib fn-ref、fun 字段/全局；对象指针 + 字符串键 `__FunVal` interface_map 与 .so 无关）。遗留小项：write_fun async 标志 roundtrip、is_funval 序列化、bound_type_qualified_name 的 FunctionKind 分支。

## 优化路径（未做）

- `__FunVal` 查找 = interface_map strcmp 扫描（与接口调用同款）→ 可缓存进 `EmperorClassMetadata.virtual_method_table`（现恒 null）直达。
- 绑定方法 invoker 缓存（同 receiver 复用）→ 修 `==` 语义 + 省分配。
- lambda 无捕获时可退化为常量单例（同静态引用机制）。

## 编译器源码策略

技能表（penguinang-coding）的 lambda/函数值禁令**已标注新解除**（EP pass2+ 能编译它们了），但编译器源码仍不主动使用 — 保守，整条 bootstrap 链保持安全子集。
