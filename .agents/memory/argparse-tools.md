# argparse 注解 + ArgInfo 双单元 #class + penguin-tools（2026-09-13）

分支 `feature/argparse-tools`（实施自 `.agents/plans/argparse-tools.md`）。提交 1cc4a1d2（编译器机制）→ 19c056e1（argparse）→ 21c12e0b（Formatter/Mangling 进 lib）→ 7596ada0（tools+Makefile）→ 4991f430（路径引用）。

## 机制 A：类成员级字段注解（`#name(args) <field>;`）

- `Parser.parse_metaCallDefinition`：`#name(args)` 后若下一 token 是 Identifier（字段起始）而非 `;`/`{`，用 `parse_classDeclaration` 解析字段存入 `mc.trailing_definition`（原死字段），并把**字段名作为追加的 string 字面量参数**注入 `mc.arguments` —— #fun 签名必须以 `field: string` 收尾（`bind_meta_args` 的 string 路径零改动）。纯语法扩展；`output`/`input` 等端口关键字不是 Identifier，注解字段名不能是它们（关键字字段本来就不合法）。
- `SemanticMetaRewrite.collect_resolved_definitions`：meta_call_def 处理完后把 `trailing_definition` push 到 `out`（排在 splice 出的 marker 函数之后，按源码序进入全部 9 个 pass）。
- 生成的 marker：`fun _parse_arg_<f>() -> mut std.ArgInfo`（`mut` 返回值是必须的——生成代码要补 `is_multi`，immutable 返回值进不了 mut 绑定）。无 `this` 成员函数 = 静态成员，`opts._parse_arg_x()` receiver 丢弃可调（SemanticBindExpressions:4535）。

## 机制 B：`#class` 双单元

- `collect_resolved_definitions` 把 `meta_class_def` 解包成 `class_def` push（先 `rewrite_meta_in_def_container`）；prepass 的 `collect_meta_class_defs` 在重写**之前**运行 → unit B 可用性不变。BuildScopes 的占位分支仅剩兜底。
- `#class` 字段无初始化器合法（IRGenerator 对 initializer 有 is_some 守卫，零值默认）。

## argparse 设计要点（防再踩坑）

- **生成器在特化重绑时才 JIT 运行**：`#template(T) fun parse_args()` 体内 `#argparse_parse(T, "opts", 0)` 首绑被 `meta_call_has_deferred_type_arg` 挡成占位字面量 0，特化 catch_up 重放时 T 具体 → JIT。`skip` 必须是**编译期常量**（i64 kind 的 meta 参数只接受常量），所以 `parse_args_after_subcommand<T>()` 单独存在而非运行时传 skip。
- **类型判别**：JIT 时刻 field 的 display_name 可能带 `mut ` 前缀、类名可能短名（`Vector<string>`）或全限定（`std.Vector<string>`）——`_arg_kind_of` 先剥 `mut ` 再取 `<` 前基名、两种拼写都收。
- **required/short/long 只存在于 ArgInfo 运行时值里（方法体反射不可读）**：选项匹配用运行时 helper `std._arg_match_option(specs, a)`（返回 spec 下标），生成代码按下标 if/else 分派；required 检查用平行 `Vector<bool>` seen + 运行时 `_arg_check_required`。重复短/长名按声明序先匹配。
- **opt_code 链首分支判定是"首个非位置字段"而非 k==0**（首个字段可能是 #pos_arg）——首分支 `if`、其余 `else if`，否则悬空 else-if 解析错误。
- **坑**：#fun 与运行时 fun 里 `let x` 后重赋值必须 `mut`（`head`/`start`/`last_dot`/`kind` 各踩一次）；`let mut v = new _utils.List<T>()` 必须 mut 绑定（push 是 mut this）；`output` 是端口关键字不能当字段名。
- 默认值语义：default_text 仅帮助显示；运行时默认值 = 字段初始化器；Vector 字段被生成代码重置为 fresh vector（不要给初始化器）。

## Formatter/Mangling 进编译器 lib

- `src/ast/Formatter.penguin`（`emperor.format_penguin_text`）+ `src/bound/Mangling.penguin`（`base62_decode_string`/`demangle_name`/`mangle_name`）：**只进 Pass2+Lib 源集，不进 Pass1**（export 标记非 ANTLR 安全；纯工具，编译器自身无人调用）。libmeta 的 keep set = export 标记 + 闭包，故必须 export。LspQuery 已删本地实现改调 lib；`Tests/LspTest/Formatting.md` 是回归守卫。
- mangle 序列化真相（BoundType.penguin）：payload = `<global>.基名<参数…>`；**每个非基本类型参数递归自带 `<global>.` 根**（`Vector<JsonValue>` → `<global>.std.Vector<<global>.std.JsonValue>`），**基本类型裸拼**（`u8`/`string`）；值参数 `i64:5`/`bool:t`/`string:abc`/`double:`/`obj:`。demangle 输出剥根 + 值美化；mangle 严格接受 demangle 输出重建全点分符号（含 `.method` 后缀）。selftest 用 libmeta 真实 224 个 instances 锁 round-trip。
- dynlib.penguin 补了 `export read_lib_footer/read_meta` + `export class LibFooter/LibMeta`（tools 的 meta 子命令消费）。

## penguin-tools / Makefile

- `EmperorPenguin/tools/`：main 分发（argv[0] 子命令）+ mangle_tool/libmeta_dump/format_tool，全部 `namespace tools`（顶层定义是每文件匿名命名空间，跨文件必须共享命名空间）；选项类用 `std.parse_args_after_subcommand<T>()`（argv[0] 是子命令字）。linux 经 `--lib` 消费 release dynlib（formatter/mangle/libmeta/vector/json 全来自 lib，零重复代码）；win 是 PenguinToolsWin 单体。
- Makefile：新增 `tools`/`tools_linux`/`tools_win`/`tools-test`/`unittest`；`all: bootstrap→lsp→tools→unittest→test`。改名：`build/linux/emperor→emperor_penguin`、`build/lsp(.ll)→build/linux/penguin-lsp(.ll)`（删除 `build/libemperorpenguin.penguin-lib` 复制目标，lib 与 exe 同目录、rpath $ORIGIN 语义不变）。LspTest md（19 个）/Readme/extension.ts/发布 smoke 同步。
- **环境坑**：ZCode 沙箱会把 `MAKE` 环境变量设成 AppImage 路径，`make all` 的递归 `$(MAKE)` 会炸 —— 用 `env -u MAKE make all`。
- 存量缺陷顺手修复：def 位置 #fun 调用**参数少于声明**会 JIT 出垃圾指针段错误（`#g();` 对 `#fun g(s: string)`）——`bind_meta_args` 加 too-few 检查（trailing-block 填 unstructured_ast 除外），守卫 `Tests/MetaProgramming/MetaFunTooFewArgs.md`。

## Formatter 两条例外(2026-09-13 追加,cd022a7)

用户要求 `emperor.format_penguin_text`(src/ast/Formatter.penguin):
1. **泛型尖括号不加空格**:`_fmt_scan_generic(items, i)` 对每个 `<` 做有界前瞻(≤128 item)——扫描到配对 `>` 即为类型括号;中途遇 `(`/`)`/`;`/`{`/`}`/`&&`/`||`/`==`/`!=`/`<=`/`>=` 判为比较运算(解决 `if (a < b)`、`a < b && c > d`)。注释透明;`Foo<A, B>` 逗号后空格保留(代码库风格)。旧实现的 `after_new`/cast 特判有 bug(`new Foo<T>` 加空格),已由分类器取代。输出恒可重分词:lexer 无 `>>` token、`>` 永不邻接 `=`(`=` 走正常空格规则)。
2. **#meta 代码逐字保留**:从原文切片 verbatim 输出——`#fun`/`#class`/`#specializing` 定义(`#fun` 先跳过参数括号组再找 body `{`,lambda 默认参数不干扰)与 `#name(...)` 调用(含尾随 `{ ... }` 块);豁免集(正常重排):`#if/#elif/#else/#for/#while/#break/#continue` 与 `#template`。切片 = [首 item 偏移, 下一 item 偏移),偏移由 1-based line/col + line_starts 表计算;切片尾部空白裁剪(最后一个 `\n` 后截断,缩进由 depth 重新发出)。注解形态 `#arg(...)\nfield` 的字段仍正常格式化。区域 emit 前要补 at_line_start 缩进或 mid-line join 空格(`#` 在 no_space_after 中,`(#mk(1))` 粘连)。

**坑**:源码 `Vector<string>=new` 被 lexer 贪婪合并成 `>=` 单 token——formatter 收到什么就保留什么,这是输入拼写问题不是 formatter bug;真实代码写 ` = ` 即可。

## 通用尾随定义机制(2026-09-14 最终修订,15d61b89)

用户最终指令:字段注解不应是"类成员专用口子",要成为**通用机制**——任意 def 位置 `#fun(args)` 后直接跟定义时,该定义交由 #fun 处置。落地:

- **Parser**:注解形态现在把字段的**原文(token 空格拼接,含 `;`)**写入 `trailing_block_raw`(复用 `{...}` 块的既有 unstructured_ast 交付通道,零新管道),同时保留字段名 string 参数注入;与 `{...}` 块互斥(is_none 守卫)。新 `parse_definitions_unit`:create_definition 的解析入口,分发器 = 顶层分发 + Identifier→字段(顶层定义从不以裸标识符开头,全局变量是 let 引导,故纯增量),接受定义列表。
- **create_definition 多定义**:1..N 个定义注册为 `definition_group`(AstToken.defs);`try_splice_meta_fun_def` 经 `get_ast_definition_list` 按序展开注入;`get_ast_definition` 对组返回首元素(兼容)。**#fun 拥有尾随定义**:collect_resolved_definitions 不再单独 push trailing_definition——#fun 必须经 create_definition 重发射,丢弃文本即删除字段。
- **`compiler().get_definition_kind(token) -> string`**:返回 Definition 变体名("class_field"/...),组取首元素;#fun 用 create_definition+kind 做"trailing 是否字段"的形状校验。管线:meta_runtime ICompiler/CompilerContext → MetaEngine unit B extern 声明 → MetaHost 响应器。
- **bind_meta_args**:trailing 有文本但没有未填的 unstructured_ast 参数 → 报错(此前静默丢弃)。
- argparse `#arg`/`#pos_arg` 签名加 `field_src: unstructured_ast` 尾参,返回"marker 函数+原字段"多定义。Argparse*.md 14 例在新机制下全绿(语法不变)。
- **坑**:`let single: mut _utils.List<Definition> = defs_opt.some;` 是 E_MUTABILITY(Option 载荷不可变 → mut 绑定);不可变 List 直接传 `collect_resolved_definitions` 的不可变 `src` 参数即可。
- **坑**:改编译器源后 `build/linux/penguin-lsp` 若不重编,LSP 测试全挂——unit B 在**运行时从磁盘读 meta_runtime.penguin**(read_compiler_source),新源码 + 旧二进制(缺响应器符号)= E_INTERNAL "Function call has no callee symbol"。改 meta_runtime/MetaHost 后必须 `make lsp`(以及 `make tools`)。
- BabyPenguin.Tests 的 ComplexTest.LinkedListTest / ProjectTest_WithoutSources 偶发并发 flaky(全量跑挂、单独跑过、与 penguin 侧改动无关)——重跑即绿,勿误判。

## win 版 tools 构建失败 + 未知 #fun 静默丢弃(2026-09-19)

- **根因**:`PenguinToolsWin.penguins` 从 e71264fb 起就漏了 `argparse.penguin`(linux 版 PenguinTools.penguins 有)。`#arg`/`#pos_arg` 这类 def 位 #fun **拥有**尾随字段定义(#fun 经 create_definition 重发"marker 函数+原字段")——#fun 不存在时字段被静默丢弃,用户侧表现为 `Type 'mut NameOptions' has no member 'names'` 级联 + `std.argv()` 未解析(void→E_MUTABILITY)。修复 = win 项目源列表加 `"../../EmperorPenguin/std/penguin/argparse.penguin"`(Makefile TOOLSWIN_SRC 的 `../../%` 过滤自动归一)。**教训:往 linux 项目加 stdlib 源时必须同步 win 单体项目;两份源列表无单一真相,极易漂移。**
- 附带现象:坏状态下编译 stderr 出现 `[EmperorPenguin Dynlib] warning: template def in non-shipped file (core_builtin.penguin)` ×9——未解析泛型经 monomorphize 走了 libmeta 表 build() 路径(collect_defs 对未 ship 的 core_builtin 模板告警);argparse 就位后消失。见到该告警 = 有泛型名没解析到,先查源列表。
- **未知 #fun 语义收紧(用户指令:报错而非静默丢弃)**:
  - def 位(`SemanticBindMetaCalls.try_splice_meta_fun_def`):`find_meta_function` 落空 → `E_RESOLVE_SYMBOL "unknown meta function '#name' (no #fun with this name is defined)"` 并消费(返回 true),尾随定义随之报错路径不再静默消失。unit B 的 def 位 skip 分支在其之前,不受影响。
  - 表达式/语句位(`bind_meta_call` 落空分支):unit A 同样报 E_RESOLVE_SYMBOL;**unit B 保持 passthrough**(rewrite_meta_call_dispatch 的"unknown -> passthrough"是元地既定语义,`#typeof` 未解析穿透依赖它)。原先落空构造的 BoundMetaCallExpression 下游无任何 lowering,等于静默吞掉——现已删除该死代码。
  - 守卫测试:`Tests/MetaProgramming/MetaFunUnknownDefPosition.md`(复刻 win tools 症状)与 `MetaFunUnknownCallPosition.md`,CONTAINS 断言错误消息,修复前红/修复后绿。
- **BoundMetaTest(C# in-process)三个用例改契约**:`BindMetaCallTopLevel`/`BindMetaCallUnknownDefPositionErrors`(原 BindMetaCallBindsArguments)/`BindMetaCallInFunctionBody` 原本断言未知 #name 的 bound 透传保留(#derive_clone 属性预留)——与新契约冲突,改为断言 `result.errors.size()==1` + 透传 def 消失/initializer 为 none。注意 InitBoundBatch 是 Lazy 单编译批,一个 snippet 运行时崩(读空 definitions 的 .at)会炸掉整类 20 个测试,别被表象骗成"全类回归"。
