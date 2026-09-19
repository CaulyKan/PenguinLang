# 31. EmperorPenguin MetaProgramming

EmperorPenguin 的元编程（见[元编程规范](../specifications/11_MetaProgramming.md)）实现为编译器中的编译器：一个在用户程序（"单元 A"）编译期间被编译并 JIT 执行的"单元 B"。语言层规则在规范；本页记录各部件的位置与值如何跨界。

## MetaEngine

**`EmperorPenguin/src/meta/MetaEngine.penguin`**（1814 行），`class MetaEngine`（13 行）。状态（14–78 行）：LLVM ORC JIT 会话句柄（`jit_ctx`）、已编译函数与调用桩缓存（`compiled_names`/`compiled_ptrs`、`stub_keys`/`stub_ptrs`）、选项存储、类型令牌注册表（`type_token_keys`/`values`、`next_type_token`）、string/double 令牌注册表、AST 令牌表、`specializing_active_slots`、重入守卫（`compiling_meta_names`）。

- `init()`（89 行）：惰性调用 `__builtin.penguin_jit_create()`；失败抛可捕获的运行时错误（绝不 `exit(1)`），LSP 得以在坏文档下存活。
- `seed_meta_engine`（`src/bound/SemanticMetaRewrite.penguin` 818–855 行）：预处理开始时的一次性接线——verbose flag、`--meta-src` 输入、`type_registry`、`global_scope`、完整 `#fun` 集、`#class` 源，然后 `penguin_meta_set_active(...)` + `owner_model`。

### #fun 的 JIT 执行

JIT 是小 C API 背后的 LLVM ORC：`std/c/penguin_jit.h`（`_emperor_penguin_jit_create/_add_module/_lookup/_destroy` + 固定参数数 trampoline `call_i64_0`、`i64_i64`……）。它只被链接进带 `-enable-meta` 构建的二进制（`src/project/CompilerConfig.penguin` 37–39、113–114 行）——Makefile 为 pass2+ 二进制与 LSP 使用的同一 flag。

`compile_meta_function(bmf)`（MetaEngine.penguin 373 行）——单元 B 流水线：

1. 从 `BoundMetaFunctionDefinition` 合成普通 `FunctionDefinition`（参数种类映射在 380–422 行：`type` → `emperor.BoundType`、`ast` → `i64`、`unstructured_ast` → `string`、值类型原样、无类型 `object` → `i64`；返回映射在 428–450 行）。
2. 构建单元 B 源文本（457–491 行）：`using emperor;` + 合成的 `#fun`（+ 所有其他 `#fun`，使 `#fun`→`#fun` 裸名调用可解析；`--meta-src` 下可选裸名前向器）。
3. 用嵌套 `EmperorPenguinCompiler` 编译单元 B（`top_level_in_global = true`、`is_unit_b = true`，534–542 行），源集为 `base_meta_sources()`（172 行）：`core_builtin.penguin`、`utils.penguin`、`meta_runtime.penguin`、整个 bound 层（BoundType/BoundDefinition/BoundSymbol/BoundScope/……）、AST 层、`ErrorCode.penguin`——自包含（约 4271 行）。JIT 中的弱符号去重使重新输出的定义解析到宿主的强导出（`emperor_BoundType_fields`……）——这正是反射成为真实指针复用的原因。用户 `--meta-src` 文件逐字追加（500–514 行）。
4. `IRGenerator` + `LLVMEmitter` → LLVM IR 文本（556–568 行；verbose ≥ 2 时转储 `/tmp/unit_b.ll`），然后 `penguin_jit_add_module` + `penguin_jit_lookup`（571–585 行）。

调用经 `call_meta_function_stub`（732 行）：惰性编译的**无参调用桩** `__stub_<sanitized>_<idx>`，其 LLVM IR 把实参烘焙为常量——`type` 实参经 `declare ptr @emperor_penguin_meta_get_type(i64)` 物化、string/double 经其令牌 getter、引用实参经 `get_object`（加 `gc_pin_object`）、bool 为 i8、其余为 i64（788–860 行）。返回编组（861–920 行）：i64 直返；bool zext；double/type/string 存入模块全局 `@emperor_active_double_result` / `@emperor_penguin_active_type_result` / `@emperor_active_string_result` 并 `ret i64 0`；引用返回 ptrtoint。

## MetaHost——宿主应答器

**`src/meta/MetaHost.penguin`**（366 行，命名空间 `emperor`）：模块全局 `active_meta: mut MetaEngine`（19 行）+ `active_model`；单元 B 可见的 extern 面（`emperor.penguin_meta_*`，声明于 `meta_extern_decls()`，MetaEngine.penguin 115–155 行）：

- `penguin_meta_get_type(token)`（31 行）——令牌 → 活 BoundType；唯一残存的反射桥。
- `penguin_meta_create_expression/_definition/_parse_arguments`（53/64/125）——在元运行时对代码字符串运行真实 Lexer+Parser；把节点注册为 AST 令牌。定义解析接受类成员形状与多定义组。
- `penguin_meta_error/warn/info`（189–205）——编译期诊断路由到所属 SemanticModel（`E_META`、合成 `<meta>` 位置）。
- `penguin_meta_specialize(name)`（178）——设置由 `active_model.ensure_specialized_type(...)` 消费的 `pending_specialize_name/args` 侧信道。
- `penguin_meta_activate_impl(slot)`（210）——压入 `specializing_active_slots` 供 `#specializing` impl 注入。
- `penguin_meta_get_current_scope()`（356）——类成员重写期间的包围类。

**`src/meta/meta_runtime.penguin`**（68 行）：`interface ICompiler` + `class CompilerContext`——只编译进单元 B；每个方法都是到 `penguin_meta_*` 应答器的单行前向。这就是 `#compiler()` 返回的东西。

## 每个指令在哪里处理

词法器只产生 `Hash` token（Lexer.penguin 678 行）——没有词法层指令。

| 构造 | 处理位置 | 代码 |
|---|---|---|
| `#template(...)` | 解析器（跳过模板括号，重新按普通定义解析） | Parser.penguin 约 2200–2232 |
| `#fun` / `#class` / `#if` / `#for` / `#while` / `#specializing` / 定义位置 `#call` | 解析器 → AST `Meta*` 节点 | Parser.penguin 2200–2600 |
| `#define` / `#if` / `#elif` / `#else` / `#while`（定义 + 语句级） | MetaRewriter 预处理——硬编码折叠；条件由字面量/`#defined`/`#option` 比较折叠（`eval_meta_bool` 172、`eval_meta_string` 147）；`#while` 展开上限 10000 | SemanticMetaRewrite.penguin 260–704 |
| `#fun` 注册、`#class` 占位、`#specializing` 块 | Pass 1（build_scopes） | SemanticBuildScopes.penguin 约 405–474 |
| **类型位置**的元调用 | Pass 2（resolve_types）——`try_resolve_meta_type_specifier` | SemanticResolveTypes.penguin 856 |
| 每实例化的 `#specializing` 执行 | Pass 3（monomorphize）——`run_specializing_for_spec` / 门控 JIT 调用；激活的 impl 槽读回并注入 | SemanticMonomorphize.penguin 450–555 |
| `#fun` 调用拼接、`#sizeof`、`#__address_of`/`#__load`/`#__store`、模板实例化路由 | Pass 8c（BindMetaCallsPass） | SemanticBindMetaCalls.penguin |
| `#fun` 体内的 `#defined`/`#option`/`#define`/`#typeof`/`#compiler()`/`#error` | MetaEngine 分派（可被用户 `#fun` 遮蔽） | MetaEngine.penguin 1541–1647 |

`#for` 已解析（`MetaForDefinition`）但预处理只拼接 `#if`/`#while`/`#define`；集合迭代未实现。`MetaRewriter.run_prepass`（SemanticMetaRewrite.penguin 34–49 行）在 9 遍之前运行：收集 `#fun` → 收集 `#class` → 播种引擎 → 定义级重写（原位，保持定义索引对齐）→ 语句级重写。

## 反射——真实指针复用

单元 B 编译了编译器真实的 `emperor.BoundType`、`BoundClassFieldDefinition`、`BoundFunctionDefinition`、`BoundEnumMemberDefinition` 类；弱符号去重把它们解析到宿主强导出。`#fun` 内的 `t.fields()`/`t.methods()`/`t.variants()`/`t.is_class()`/`t.display_name()` 是对活对象的直接方法调用——没有逐操作应答器协议。类型令牌按名驻留（`get_or_assign_type_token`，MetaEngine.penguin 1044）；`#typeof` 比较是驻留指针的指针相等。计算名字的解析：`resolve_type_by_name`（1061）→ 宿主注册表 → `global_scope.lookup_type_anywhere` → **AST 回退** `resolve_type_from_ast`（1100），它从未绑定 AST 类定义构建部分 BoundType，使反射在定义拼接时（Pass 1 之前）可用——`json.penguin` 的 `#impl_json_serializable` 背后的机制。

## 什么在哪里运行

同一份编译器源码的两个构建变体决定可用性（`meta_runtime_available()`）：

* `EmperorPenguinPass1.penguins` 含 `src/meta/MetaConfigStub.penguin` → 返回 false；pass1 与 BabyPenguin 编译的构建解析 `#template` 但从不 JIT（在那里调用 `penguin_jit_*` 内建会抛错）。
* `EmperorPenguinPass2.penguins`（自举 pass2/pass3、release 编译器、LSP）换入 `std/penguin/metaconfig.penguin` → 返回 true；以 `-enable-meta` 构建，`libpenguin_jit` 被链接、所有 JIT 路径可用。

`#define`/`#if`/`#while` 折叠是硬编码的，在任何 EmperorPenguin 构建中都工作。元测试套件是 `Tests/MetaProgramming/`（100+ 例，Apply To Pass2/Pass3）。
