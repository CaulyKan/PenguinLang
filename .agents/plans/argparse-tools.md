# argparse 标准库(注解 + ArgInfo 标记函数) + penguin-tools 工具集 + 构建路径调整

状态:已实施完成(2026-09-14,最后一个提交 15d61b89;含最终通用化修订)。

核心决策记录:
- **通用尾随定义捕获**(用户指定,替代"类成员开专用口子"):def 位置的
  `#meta(args)` 后**直接跟定义**(无分号/花括号)时,该定义的原文经既有的
  `unstructured_ast` 尾参通道交付,且其**名字隐式注入**为倒数第二个 string 参数;
  `#fun` 校验形状、生成附加定义,并**同时返回附加定义与原定义**(多定义 splice)。
  argparse 的 `#arg`/`#pos_arg` 只是该通用机制之上的一层薄实现。
- argparse 语义:字段注解生成 `_parse_arg_<field>() -> std.ArgInfo` 标记函数,
  生成器用 `t.fields()` + `t.methods()` 反射关联;**不使用 set_option 注册表**。
- 允许的**编译器改动**(全部为通用机制,非 argparse 专用):① 尾随定义捕获;
  ② create_definition 支持字段形状与多定义单元;③ `compiler().get_definition()`
  响应器(形状内省);④ `#class` 同时作为 unit A 正常类编译。
- **编译器域的功能一律进 libemperor、tools 只做薄封装**(用户指定):
  `format_penguin_text`/mangle/demangle 等放 `emperor` 命名空间并编入
  libemperorpenguin,LSP 与 tools 经 `--lib` 调用同一实现,避免重复代码。
  argparse 属用户 stdlib,仍留在 `std/penguin/`(消费方按源码编译)。
- 工具二进制名 `penguin-tools`;补上缺失的 `unittest` 目标;
  `build/linux/emperor` → `build/linux/emperor_penguin`(发布副本同步改名);
  `make lsp` 输出 → `build/linux/penguin-lsp`;`make tools` 加入 `all` 与 `publish`。

---

## 1. 编译器改动(4 处,均为通用机制)

### A. 通用尾随定义捕获(def-position meta call + 跟随定义)
目标行为(用户示例):
```penguin
#fun tagged(tag: string, field: string, trailing_ast: unstructured_ast) -> ast {
    // 校验 trailing_ast 可解析为 class-field 定义(见 C 的 get_definition)
    // 生成附加定义(如标记函数)
    // 返回:附加定义 + 原 class-field 定义(见 B 的多定义 create_definition)
}
class C {
    #tagged("alpha")
    name: string = "field-ok";
}
```
- **Parser**(`parse_metaCallDefinition`,def 上下文通用——类成员/枚举成员/顶层):
  解析 `#name(args)` 后,若下一个 token 不是 `;` 也不是 `{`,而是某个定义的起始
  token,则:按当前上下文的定义分发解析**一个**定义,把其 token 原文(空格连接,
  与 `capture_meta_trailing_block` 同法)写入 `mc.trailing_block_raw`——完全复用
  既有的 unstructured_ast 交付通道,零新增 meta 侧管道;**同时把该定义的名字
  隐式注入** `bind_meta_args`(追加为 trailing 文本参数之前的 string 参数)。
  `#funs` 声明 `(..., name: string, src: unstructured_ast)` 即可接收。
- **bind_meta_args**:处理 trailing_block_raw 时,若来源是尾随定义(而非 `{...}`
  块),先 push 定义名 string 字面量再 push 原文 token(约 5 行特判;parser 在
  MetaCallExpression 上区分两种来源,可复用 vestigial 的 `trailing_definition`
  字段存"已解析的定义"以携带名字)。
- 纯语法扩展(原先为解析错误的输入);现有 `#xxx();`/`#xxx(){...};` 不变,
  零回归面。

### B. create_definition 支持字段形状 + 多定义单元
- 现状:`penguin_meta_create_definition`(MetaHost.penguin:64)走顶层解析器且
  要求**恰好一个**定义;裸字段文本顶层无匹配分支(用户已定性为编译器 bug)。
- 修复/扩展:
  1. 顶层分发回退:解析失败或 0 定义时,按**类成员上下文**重试(复用
     parse_classDefinition 的成员分发——字段/函数/impl 均可),要求恰好一个
     成员 + EOF;现有可顶层解析的输入(json 的 impl、derive_clone 的 fun)
     行为完全不变;
  2. **多定义单元**:文本解析出 N≥1 个定义时全部注册为一个组,返回组 token;
     `try_splice_meta_fun_def` 展开组内**全部**定义按序注入(#fun 由此实现
     "返回附加定义 + 原定义")。
- 守卫:`Tests/MetaProgramming/MetaCreateDefinitionField.md`(字段形状)、
  `MetaMultiDefinitionSplice.md`(多定义组)。

### C. `compiler().get_definition(token)` 响应器
- #fun 内省定义节点:MetaEngine 已有内部的 `get_ast_definition`(splice 路径在
  用),经 MetaHost/meta_runtime 暴露给 meta 代码(`emperor.Definition.class_field`
  模式匹配在 unit B 可用——MetaPrintf 对 Expression 的先例同源)。
- 用途:`#tagged` 类 #fun 对 trailing_ast 做"是否 class-field"形状校验
  (`compiler().error` 报错)。

### D. `#class` 同时作为 unit A 正常类编译
- 现状:`parse_metaClassDefinition` 复用 `parse_classDefinition`(AST 即普通
  ClassDefinition),`SemanticBuildScopes:320` 将其绑为占位符跳过;meta 侧由
  prepass 的 `collect_meta_class_defs`(在重写**之前**运行)以源码文本喂给
  unit B。
- 改动:`collect_resolved_definitions` 中将 `meta_class_def` 重写为 `class_def`
  (解包)推入 unit——收集已发生在重写前,unit B 可用性不变;BuildScopes 占位
  分支保留兜底。效果:`#class ArgInfo` 既是 meta 时数据结构又是运行时真实类。
- 守卫测试:`Tests/MetaProgramming/MetaClassDualUnitA.md`;跑全量确认现有
  #class 用例无回归。

## 2. `EmperorPenguin/std/penguin/argparse.penguin`(依赖 vector.penguin,Apply To: Pass2/Pass3)

目标语法(default 参数需字符串字面量):

```penguin
class Options {
    // help, short, long, required, default(display)
    #arg("verbose output level", "-v", "--verbose", false, "0")
    verbose: i64 = 0;

    #pos_arg("input files to process")
    files: mut std.Vector<string>;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println(cast<string>(opts.verbose));
}
```

- `#class ArgInfo { help: string; short: string; long: string; required: bool;
  positional: bool; default_text: string; }` —— 机制 D 使其成为运行时类。
- `#fun arg(help: string, short: string, long: string, required: bool,
  default_text: string, field: string, field_src: unstructured_ast) -> ast`:
  `field`/`field_src` 由机制 A 隐式注入/捕获;用 `get_definition` 校验
  field_src 为 class-field(否则 `compiler().error`);返回
  `create_definition("fun _parse_arg_<field>() -> std.ArgInfo { return new
  std.ArgInfo(...); } " + field_src)`——**标记函数 + 原字段**一次 splice
  (机制 B 多定义)。help 文本转义。`#fun pos_arg(help: string, field: string,
  field_src: unstructured_ast) -> ast` 同理(positional=true)。
  无 `this` 的成员函数即静态成员,`opts._parse_arg_x()`/
  `Options._parse_arg_x()` 均可调用(SemanticBindExpressions:4535 已支持
  receiver 丢弃)。
- `fun parse_args<T>() -> mut T { let opts: mut T = new T();
  #argparse_parse(#typeof(T), "opts"); return opts; }`
  —— `new T()` 已支持(IRGenerator.lower_new,commit 2547c45d);
  `#argparse_parse(t: type, v: string) -> ast` 在泛型体中延迟(类型参数未特化→
  占位,json 先例形状),特化重绑时 JIT 运行:遍历 `t.fields()`(声明序)+
  `t.methods()` 匹配 `_parse_arg_<字段名>`(BoundType.penguin:725 methods(),
  MetaReflectionCount 先例)生成整段解析逻辑。
- **生成语义(clap 风格)**:循环前 hoist `let __s_x = opts._parse_arg_x();`;
  `--name val`/`--name=val`/`-x val`/`-x=val`;bool 字段纯开关;
  i*/u*→string_to_int(按字段生成 `cast<uN>`),f64/float→string_to_double,
  string 直存,`std.Vector<T>` 重复 push(元素递归分派);`--` 后全位置参数;
  标量位置参数按声明序隐含 required,Vector 位置参数收集剩余;`#arg` required
  生成 seen 标志+缺失报错;`-h/--help`→`std.print_arg_help(specs, 类名)` 打印
  stdout 后 exit(0);解析错误→stderr 报错+usage 后 exit(2);成功返回已填好的
  opts。
- `export fun argv() -> mut std.Vector<string>`(包装
  `__builtin.__args_count/__args_get`);`fun print_arg_help(specs, prog)` 运行时
  共享帮助渲染(help/short/long/required/default_text 全部来自 ArgInfo 实例——
  元数据单一来源即标记函数,无 set_option 注册表)。
- 校验(generator 内 `compiler().error`):类无任何 `_parse_arg_*` 方法而调用
  parse_args、字段类型不支持;enum 按 variant 名匹配(stretch)。
- 注:default_text 仅用于帮助显示,运行时默认值仍取字段初始化器(默认构造器);
  文档注明可变集合字段需 `mut std.Vector<...>`。

## 3. 格式化器进编译器 lib(emperor 命名空间,不进 std)

- 新文件 `EmperorPenguin/src/ast/Formatter.penguin`,`namespace emperor`:
  `export fun format_penguin_text(text: string) -> string` + 私有 helper
  (`collect_comments`/`FmtItem`/`make_fmt_item`),从
  `MagellanicPenguin/LspServer/LspQuery.penguin:448-700` 迁移。
  容器改用 `_utils.List`(编译器源码惯用,避免对 vector.penguin 的依赖,
  Pass1 源集可直接收录)。
- 项目源集:加入 `EmperorPenguinPass1.penguins`、`EmperorPenguinPass2.penguins`、
  `EmperorPenguinLib.penguins`(编入 libemperorpenguin,LSP/tools 经 `--lib`
  调用);`LspServerWin.penguins`(win 单体)按 `../../` 相对路径加入。
- `LspQuery.penguin` 删除本地实现,改调 `emperor.format_penguin_text`;
  LSP(linux)无需改 sources(formatter 来自 lib)。
- `Tests/LspTest/Formatting.md` 作行为回归守卫。

## 4. mangle/demangle 进编译器 lib(emperor 命名空间)

- 新文件 `EmperorPenguin/src/bound/Mangling.penguin`,`namespace emperor`,
  与 `BoundType.penguin` 的 `base62_encode_string`/`mangle_specialization`
  (唯一的 mangle 真相源)同域:
  - `export fun base62_decode_string(s: string) -> string`(每 10 字符→u64→
    7 字节小端,去尾部 NUL padding——仓库现无解码器,新写);
  - `export fun demangle_name(mangled: string) -> string`:按 `.` 顶层分段,
    含 `$` 段解码 payload,美化(`<global>.` 前缀、`i64:5`→`5`、`bool:t`→
    `true` 等值参数);非 mangled 输入原样回显(c++filt 行为);legacy `__`
    方案不再生成、不支持;
  - `export fun mangle_name(payload_text: string) -> string`:严格接受 demangle
    的输出形式,拆 base+顶层逗号分隔 args → 重组 payload → base62 →
    `short$suffix`,保证 mangle(demangle(x)) == x。
- 同样加入 Pass1/Pass2/Lib 三个项目源集;**export 标记**(libmeta 导出规则)。
- 附带收益:编译器诊断/未来工具可复用 demangle。

## 5. 新增 `EmperorPenguin/tools/`(penguin-tools,薄封装 + dogfood)

`PenguinTools.penguins`(sources=[main.penguin, mangle_tool.penguin,
libmeta_dump.penguin, format_tool.penguin, ../std/penguin/argparse.penguin];
vector/Formatter/Mangling/dynlib/json 均来自 `--lib build/linux/libemperorpenguin.penguin-lib`
——**尽量零重复代码,tools 只做 CLI 胶水**)+ 四模块:

- **main.penguin**:子命令分发(demangle|mangle|meta|format|help),各子命令用
  `#arg`/`#pos_arg` 注解类 + `std.parse_args<...>()` dogfood:
  FormatOptions{@file, -o/--output, -i/--in-place}、
  MetaOptions{@file, -s/--symbols, --json}、
  NameOptions{@names...}(空时读 stdin,对齐 c++filt,用 std.io.stdin_lines)。
- **mangle_tool.penguin**:读入名字 → 调 `emperor.demangle_name` /
  `emperor.mangle_name` → 输出。纯 CLI 胶水。
- **libmeta_dump.penguin**(meta 子命令):读取复用 lib 中 dynlib.penguin 的
  `read_lib_footer`/`read_meta`(缺 export 则补,改动极小)+ json.penguin;
  渲染为本模块私有(单一消费者):默认摘要(lib 名/版本/deps/instances 数/
  symbols 按 kind 计数/verbatim source 列表),`-s` 全量符号明细,`--json`
  原始文档;instances 用 `emperor.demangle_name` 美化。
- **format_tool.penguin**:std.io.read_text → `emperor.format_penguin_text` →
  默认 stdout / `-o` 写文件 / `-i` 原地写回;`-o`/`-i` 互斥报错。

## 6. Makefile

- **make tools**(host):`build/tools/penguin-tools.ll`(用 release 编译器
  `build/linux/emperor_penguin_llvm_emitter` + `--lib
  build/linux/libemperorpenguin.penguin-lib` 编译);`build/linux/penguin-tools`
  (`emperor link ... -enable-meta --consumer-lib`,依赖 C_RT);
  **tools_win**:PenguinToolsWin.penguins 单体(镜像 LspServerWin;linux host
  经 `-target=win64` 交叉编译)。
- **build/linux/emperor → build/linux/emperor_penguin**:规则、release_linux
  依赖、publish 部署副本同步改名(含 extension.ts 引用)。
- **make lsp → build/linux/penguin-lsp**:build/lsp.ll→build/linux/penguin-lsp.ll、
  build/lsp→build/linux/penguin-lsp;删除 `build/libemperorpenguin.penguin-lib`
  复制目标(release lib 本就同目录,rpath $ORIGIN 语义不变);win-host 复制
  路径、publish cp、注释同步。
- **新增 `unittest`**:`dotnet test`(tee build/logs/unittest.log),修复 pending
  的 `make all` 报错(未提交改动引用了不存在的目标)。
- **all: bootstrap → lsp → tools → unittest → test**;publish 的
  PUBLISH_TARGETS 加入 tools 并部署;`.PHONY` 与头部注释更新。
- 新增 `make tools-test`:运行 `EmperorPenguin/tools/selftest.sh` 黄金比对。

## 7. 路径引用同步

- `Tests/LspTest/*.md` 全部 19 个:`Args: build/lsp` → `Args: build/linux/penguin-lsp`;
  `Tests/Readme.md`、`MagellanicPenguin/LspServer/README.md`。
- vscode `extension.ts`:server/linux/emperor → emperor_penguin。
- AGENTS.md/CLAUDE.md:新目标、新路径、argparse 章节、tools 章节。

## 8. 测试

- `Tests/MetaProgramming/MetaTaggedDefinition.md`:**用户的 tagged 示例**作为
  通用尾随定义捕获的守卫(#tagged 校验 class-field 形状、生成附加定义、
  附加定义+原字段都存在且可用)。
- `Tests/MetaProgramming/MetaCreateDefinitionField.md` +
  `MetaMultiDefinitionSplice.md`:机制 B 两半的守卫。
- `Tests/MetaProgramming/MetaClassDualUnitA.md`:机制 D 守卫。
- `Tests/StdlibTest/Argparse*.md` 约 10 例(Compile Args 携带
  argparse.penguin+vector.penguin,Apply To: Pass2, Pass3):注解基础+默认值、
  短/长/`=` 形式、required 缺失、未知选项、位置参数标量/Vector 收集、`--`
  分隔、bool 开关、help 精确输出、转换失败;byte-exact 断言 stdout+exit code。
- `EmperorPenguin/tools/selftest.sh`:4 子命令黄金测试;demangle/mangle 以
  libmeta 真实 instances 往返验证(mangle(demangle(x))==x)。
- 全量 `make all` 一次跑跑通(Parser/MetaHost/MetaEngine/SemanticMetaRewrite/
  BuildScopes 变更 → bootstrap 重收敛),日志 tee;基线变化用
  `make baseline_test`。

## 9. 实施顺序(里程碑提交)

1. 编译器通用机制 A+B+C(尾随定义捕获、create_definition 字段/多定义、
   get_definition 响应器)+ tagged 守卫测试,`make bootstrap` 收敛、全量绿。
2. 机制 D(#class 双编译)+ 守卫测试。
3. argparse.penguin + Argparse*.md 测试(pass2/pass3)。
4. Formatter.penguin / Mangling.penguin 进 lib,LSP 切换,Formatting.md 回归。
5. tools 四模块 + selftest + Makefile(tools/unittest/改名/lsp 路径/all/publish)。
6. 路径引用与文档同步;最终全量 `make all` + 更新 `.agents/memory`。

## 10. 风险与对策

- 尾随定义捕获与名字注入的解析分支:纯语法扩展(原解析错误输入),现有
  `#xxx();`/`#xxx(){...};` 不受影响;tagged 守卫测试 + 全量套件。
- 多定义 create_definition 的组注册与 splice 展开:保持"恰好一定义"路径为
  组大小=1 的特例,现有 json/derive 单定义调用不变;守卫测试覆盖。
- #class 双编译的存量影响:现有 #class 用例若断言"不产生运行时类"需按新语义
  更新(行为变更是本需求的目的);全量套件验证。
- 泛型体 placeholder 在 `#argparse_parse(#typeof(T), ...)` 语句位置:#printf
  语句位置 splice 同款,先例直接成立。
- 短/长名唯一性只能在运行时发现(ArgInfo 值 meta 时不可见——方法体反射不可读):
  文档注明,重复项由生成代码的 if/else 顺序决定归属。
- Formatter/Mangling 进 Pass1 源集的依赖面:用 `_utils.List`、只依赖 Lexer/
  BoundType 已有代码;若 Pass1 收录受阻,降级为 Pass2+Lib 并在 bootstrap 首轮
  编入(pass1 编译 pass2 源集时即验证)。
- mangle 往返以 libmeta 真实实例锁定,不靠推测。
