# 31. EmperorPenguin MetaProgramming

EmperorPenguin 的元编程（见[元编程规范](../specifications/11_MetaProgramming.md)）实现为编译器中的编译器：一个在用户程序（"单元 A"）编译期间被编译并 JIT 执行的"单元 B"。语言层规则在规范；本页记录各部件的位置与值如何跨界。

## MetaEngine

**`EmperorPenguin/src/meta/MetaEngine.penguin`**（1916 行），`class MetaEngine`（13 行）。状态（14–90 行）：LLVM ORC JIT 会话句柄（`jit_ctx`）、已编译函数与调用桩缓存（`compiled_names`/`compiled_ptrs`、`stub_keys`/`stub_ptrs`）、文件级 define 视图键 `option_view_file`（引擎自身**不持有**存储——`has_option`/`get_option`/`set_option` 桥接到所属 SemanticModel 的统一存储，按 `#fun` 的**定义文件**为键，因此一个 `#fun` 看到自己文件的 define 加 CLI 播种，绝看不到别的文件的）、类型令牌注册表（`type_token_keys`/`values`、`next_type_token`）、string/double 令牌注册表、AST 令牌表、`specializing_active_slots`、重入守卫（`compiling_meta_names`）。

- `init()`（104 行）：惰性调用 `__builtin.penguin_jit_create()`；失败抛可捕获的运行时错误（绝不 `exit(1)`），LSP 得以在坏文档下存活。
- `seed_meta_engine`（`src/bound/SemanticMetaRewrite.penguin` 1225 行）：预处理开始时的一次性接线——verbose flag、`--meta-src` 输入、`type_registry`、`global_scope`、完整 `#fun` 集、`#class` 源，然后 `penguin_meta_set_active(...)` + `owner_model`。

### #fun 的 JIT 执行

JIT 是小 C API 背后的 LLVM ORC：`std/c/penguin_jit.h`（`_emperor_penguin_jit_create/_add_module/_lookup/_destroy` + 固定参数数 trampoline `call_i64_0`、`i64_i64`……）。它只被链接进带 `-enable-meta` 构建的二进制（`src/project/CompilerConfig.penguin` 39–41、180–181 行）——Makefile 为 pass2+ 二进制与 LSP 使用的同一 flag。

`compile_meta_function(bmf)`（MetaEngine.penguin 390 行）——单元 B 流水线：

1. 从 `BoundMetaFunctionDefinition` 合成普通 `FunctionDefinition`（参数种类映射：`type` → `emperor.BoundType`、`ast` → `i64`、`unstructured_ast` → `string`、值类型原样、无类型 `object` → `i64`；返回映射紧随其后）。
2. 构建单元 B 源文本（483–523 行）：`using emperor;` + 合成的 `#fun`（+ 所有其他 `#fun`，使 `#fun`→`#fun` 裸名调用可解析；`--meta-src` 下可选裸名前向器）。
3. 用嵌套 `EmperorPenguinCompiler` 编译单元 B（`top_level_in_global = true`、`is_unit_b = true`，566–567 行），源集为 `base_meta_sources()`（192 行）：`core_builtin.penguin`、`utils.penguin`、`meta_runtime.penguin`、整个 bound 层（BoundType/BoundDefinition/BoundSymbol/BoundScope/……）、AST 层、`ErrorCode.penguin`——自包含。JIT 中的弱符号去重使重新输出的定义解析到宿主的强导出（`emperor_BoundType_fields`……）——这正是反射成为真实指针复用的原因。用户 `--meta-src` 文件逐字追加（524–540 行）。
4. `IRGenerator` + `LLVMEmitter` → LLVM IR 文本（583–598 行；verbose ≥ 2 时转储 `/tmp/unit_b.ll`），然后 `penguin_jit_add_module` + `penguin_jit_lookup`（600–612 行）。

调用经 `call_meta_function_stub`（783 行）：惰性编译的**无参调用桩** `__stub_<sanitized>_<idx>`，其 LLVM IR 把实参烘焙为常量——`type` 实参经 `declare ptr @emperor_penguin_meta_get_type(i64)` 物化、string/double 经其令牌 getter、object 实参以 1 起始的**句柄**表示、经 `penguin_meta_get_object`（MetaHost 里的注册表查询——不经过裸地址）还原为活引用，bool 为 i8、其余为 i64。返回编组：i64 直返；bool zext；double/type/string 存入模块全局 `@emperor_active_double_result` / `@emperor_active_type_result` / `@emperor_active_string_result` 并 `ret i64 0`；object 返回在**桩内**就登记进句柄注册表（`call i64 @emperor_penguin_meta_register_object(ptr %r)`）——引用在宿主侧从不变成整数，由 GC 追踪的注册表（`List<IReferenceType>`）保活，无需 pin。

## MetaHost——宿主应答器

**`src/meta/MetaHost.penguin`**（468 行，命名空间 `emperor`）：模块全局 `active_meta: mut MetaEngine`（19 行）+ `active_model`；单元 B 可见的 extern 面（`emperor.penguin_meta_*`，声明于 `meta_extern_decls()`，MetaEngine.penguin 130–170 行）：

- `penguin_meta_get_type(token)`（123 行）——令牌 → 活 BoundType；唯一残存的反射桥。
- `penguin_meta_create_expression/_definition/_parse_arguments`（153/164/225）——在元运行时对代码字符串运行真实 Lexer+Parser；把节点注册为 AST 令牌。定义解析接受类成员形状与多定义组。
- `penguin_meta_error/warn/info`（295–307）——编译期诊断路由到所属 SemanticModel（`E_META`、合成 `<meta>` 位置）。
- `penguin_meta_specialize(name)`（278）——设置由 `active_model.ensure_specialized_type(...)` 消费的 `pending_specialize_name/args` 侧信道。
- `penguin_meta_activate_impl(slot)`（310）——压入 `specializing_active_slots` 供 `#specializing` impl 注入。
- `penguin_meta_get_current_scope()`（458）——类成员重写期间的包围类。

**`src/meta/meta_runtime.penguin`**（71 行）：`interface ICompiler` + `class CompilerContext`——只编译进单元 B；每个方法都是到 `penguin_meta_*` 应答器的单行前向。这就是 `#compiler()` 返回的东西。

## 每个指令在哪里处理

词法器只产生 `Hash` token（Lexer.penguin 684 行）——没有词法层指令。

| 构造 | 处理位置 | 代码 |
|---|---|---|
| `#template(...)` | 解析器（跳过模板括号，重新按普通定义解析） | Parser.penguin 约 2252–2290 |
| `#fun` / `#class` / `#if` / `#for` / `#while` / `#specializing` / 定义位置 `#call` | 解析器 → AST `Meta*` 节点 | Parser.penguin 约 1130–1800 |
| `#define` / `#if` / `#elif` / `#else` / `#while`（定义 + 语句级） | MetaRewriter 预处理——针对**文件级** define 存储做硬编码折叠（代码 `#define` 从所在行可见到文件尾；CLI `--define` 为每个文件播种）；条件由字面量、`#defined("K")` 与裸 define 名比较折叠（`eval_meta_string` 272、`eval_meta_bool` 287）；同文件重定义报 `E_DUPLICATE_SYMBOL`；`#while` 展开上限 10000 | SemanticMetaRewrite.penguin（1333 行；定义级 `rewrite_meta_definitions` 569、语句级 `rewrite_meta_statements_inplace` 888） |
| `#fun` 注册、`#class` 占位、`#specializing` 块 | Pass 1（build_scopes） | SemanticBuildScopes.penguin 约 316–474 |
| **类型位置**的元调用 | Pass 2（resolve_types）——`try_resolve_meta_type_specifier` | SemanticResolveTypes.penguin 924 |
| 每实例化的 `#specializing` 执行 | Pass 3（monomorphize）——`run_specializing_for_spec` / 门控 JIT 调用；激活的 impl 槽读回并注入 | SemanticMonomorphizeSpecializing.penguin 28 |
| `#fun` 调用拼接、`#sizeof`、模板实例化路由 | Pass 8c（BindMetaCallsPass） | SemanticBindMetaCalls.penguin |
| `#fun` 体内的 `#defined`/`#define`/`#typeof`/`#compiler()`/`#error` | MetaEngine 的单元 B 重写 `rewrite_meta_calls_in_expr`——`#defined(K)` 按 `#fun` 定义文件的 define 视图折叠为 BoolLiteral、`#define(K,V)` 写入；`#typeof`/`#compiler`/未知则透传（可被用户 `#fun` 遮蔽） | MetaEngine.penguin 1441–1750 |

`#for` 已解析（`MetaForDefinition`）但预处理只拼接 `#if`/`#while`/`#define`；集合迭代未实现。`MetaRewriter.run_prepass`（SemanticMetaRewrite.penguin 168 行）在 9 遍之前运行：收集 `#fun` → 收集 `#class` → 播种引擎 → 定义级重写（原位，保持定义索引对齐）→ 语句级重写。

## 反射——真实指针复用

单元 B 编译了编译器真实的 `emperor.BoundType`、`BoundClassFieldDefinition`、`BoundFunctionDefinition`、`BoundEnumMemberDefinition` 类；弱符号去重把它们解析到宿主强导出。`#fun` 内的 `t.fields()`/`t.methods()`/`t.variants()`/`t.is_class()`/`t.display_name()` 是对活对象的直接方法调用——没有逐操作应答器协议。类型令牌按名驻留（`get_or_assign_type_token`，MetaEngine.penguin 1096）；`#typeof` 比较是驻留指针的指针相等。计算名字的解析：`resolve_type_by_name`（1113）→ 宿主注册表 → `global_scope.lookup_type_anywhere` → **AST 回退** `resolve_type_from_ast`（1152），它从未绑定 AST 类定义构建部分 BoundType，使反射在定义拼接时（Pass 1 之前）可用——`json.penguin` 的 `#impl_json_serializable` 背后的机制。

## 什么在哪里运行

同一份编译器源码的两个构建变体决定可用性（`meta_runtime_available()`）：

* `EmperorPenguinPass1.penguins` 含 `src/meta/MetaConfigStub.penguin` → 返回 false；pass1 与 BabyPenguin 编译的构建解析 `#template` 但从不 JIT（在那里调用 `penguin_jit_*` 内建会抛错）。
* `EmperorPenguinPass2.penguins`（自举 pass2/pass3、release 编译器、LSP）换入 `std/penguin/metaconfig.penguin` → 返回 true；以 `-enable-meta` 构建，`libpenguin_jit` 被链接、所有 JIT 路径可用。

`#define`/`#if`/`#while` 折叠是硬编码的，在任何 EmperorPenguin 构建中都工作（BabyPenguin 用自己的 C# 侧 `MetaSplicer` 拼接同样的构造）。元测试套件是 `Tests/MetaProgramming/`（116 例，Apply To Pass2/Pass3；`#define`/`#if` 用例也在 BabyPenguin 上运行）。
