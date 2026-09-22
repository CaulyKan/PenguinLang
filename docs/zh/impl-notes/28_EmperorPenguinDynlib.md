# 28. EmperorPenguin 动态库与 libmeta v1 格式

`.penguin-lib` 是一个原生共享对象（ELF `.so` / PE `.dll` / Mach-O `.dylib`），
其二进制内容之后附加了一段 JSON 元数据块以及一个 ASCII 尾部标记：

```
<original .so bytes>
<JSON metadata document>
PENGUINLIB:<meta_offset>:<meta_size>\n
```

元数据（`emperor-libmeta` v1）是一个 C# 元数据风格的**结构化符号表**，
而非嵌入源码。存在两种消费方（见[消费](#consumption)）；
二者产出的 `.ll` 逐字节一致。

参考实现：`EmperorPenguin/std/penguin/dynlib.penguin`
（写入方 `serialize_symbols`，读取方 `read_meta` / `libmeta_direct_inject`）。
ANTLR 安全的 `DynlibStub.penguin`（pass1 构建）禁用了上述全部功能。

## 顶层文档

```json
{"format":"emperor-libmeta","version":1,
 "name":"libemperorpenguin","deps":["libcore","..."],
 "instances":["<global>.std.Vector$2FibnjNpwi...", "..."],
 "symbols":[ ... ]}
```

| 键          | 含义                                                                                 |
| ----------- | ------------------------------------------------------------------------------------ |
| `format`    | 始终为 `"emperor-libmeta"`。                                                          |
| `version`   | `1`。读取方会拒绝其他版本。                                                           |
| `name`      | 库名（`link_lib` 也会将其盖写为 `.so` 的 SONAME）。                                   |
| `deps`      | 本库构建时所依赖的库名（`--lib` 输入，传递闭包）。                                    |
| `instances` | 该 .so 已包含的每个泛型特化的 mangled `full_name`。消费方复用这些实例，               \
              而不是重新特化（只声明、不定义）。                                                     |
| `symbols`   | 有序的符号/源码条目，见下文。                                                         |

## 符号条目（`symbols[]`）

| `kind`     | 字段                                                              | 说明                                                                   |
| ---------- | ----------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `ns`       | `name`                                                            | 命名空间声明（仅用于分组）。                                            |
| `class`    | `ns`、`name`、`fields[]`、`methods[]`、`impls[]`                  | 字段：`{n, t, mu}`；方法/实现见下文。                                   |
| `iface`    | `ns`、`name`、`methods[]`、`impls[]`                              | 与 class 相同的结构，但没有字段。                                       |
| `enum`     | `ns`、`name`、`members[]`、`methods[]`、`impls[]`                 | 成员：`{n, p（载荷类型或 null）, v（判别值）}`。                        |
| `fun`      | `ns`、`name`、`params[]`、`ret`                                   | 标志位：`x`（extern）、`p`（纯函数）、`nw`（`new` 构造器）、`st`（静态）。|
| `alias`    | `ns`、`name`、`t`                                                 | `type` 类型引用。                                                       |
| `implfor`  | `ns`、`iface`、`for`、`methods[]`                                 | 顶层 `impl X for Y` 边（值类型分类所需）。                              |
| `global`   | `ns`、`name`、`t`、`m`、`init`                                    | 全局变量，附带逐字初始化表达式文本。                                    |
| `source`   | `name`、`text`                                                    | 模板/元编程文件的逐字源码（见保留集）。                                 |

函数（既包括顶层 `fun`，也包括嵌套方法，后者用 `n` 代替 `kind/ns/name`）：

- `params[]`：`{n, t, m?}` —— `m` 仅出现在无类型的 `this` 参数（`mut this`）上；普通参数的可变性**标注在类型上**（`x: mut T` —— 绑定层级的 `mut x: T` 不是合法语法）。
- `ret`：返回类型 `<T>`。
- `impls[]` / `implfor`：`{iface: <T>, methods: [...], vt: ["<impl method full_name>"|null, ...]}` —— `vt` 是发布方的**预构建虚表**（pass 6 的产物，按槽位排序）；直接注入会逐字采用它，并对 lib 定义跳过 pass 6。

## 类型编码 `<T>`

```json
{"b":"<base>", "a":[<arg>...], "m":0|1}
```

- `b` —— 原始类型注册表名（`"i32"`、`"string"`、……）或**不带** `<global>.` 前缀的 class/enum/interface 全名（`"emperor.BoundType"`、`"_utils.List"`）。不使用 `display_name()`：它带有可变性前缀，无法往返还原。
- `m` —— 类型可变时为 `1`，不可变时省略。
- `a` —— 泛型实参，仅在非空时出现：
  - 类型实参：`{"t": <T>}`
  - 值实参：`{"v": {"k":"i64|bool|string|double|obj", ...}}`，值存于 `i` / `b` / `s` / `d`（double 以十进制字符串表示）/ `u`（唯一名）。
- 函数类型：`{"k":"fun", "a":[<param T>..., <ret T>], "ay":1?}`（`ay` = async），没有 `b`。
- 字段可变性是独立的三态 `mu`：`2` = `mut`，`1` = `!mut`，省略 = 自动（类型本身按不可变序列化，因此物化出的拼写不会重复叠加）。
- 全局变量的 `m` 镜像源码：`let mut` + 非 mut 类型会告警并丢弃 mut。

## 保留集（export 激活）

库的元数据携带：

1. 每个 `export` 标记的定义（`export namespace` 会级联到全部成员），以及被保留的签名、字段、enum 载荷、impl 边和全局初始化器所引用类型的**闭包**（被引用的类型即使自身未导出也会保留 —— 消费方需要其声明）；
2. 每个全局变量 —— 消费方必须重新定义并重新初始化它们，以便通过 GOT 插入（exe 侧的副本经由 GOT 插入 .so 侧的副本）；
3. 每条顶层 `impl X for Y` 边 —— 值类型（ICopy/IRef）分类必须与发布方一致；
4. 每个包含模板/元编程构造（`#template` / `#fun` / `#specializing` / `#if` ……）的库文件的逐字源码 —— 消费方重新绑定这些文件，以便在本地单态化**新的**泛型实例（`instances[]` 中已随库交付的实例按只声明、不定义复用；一个 `$` 修饰的特化若其模板并非来自库源码，则为 `E_DUPLICATE_SYMBOL` —— 例外是 `<global>.__builtin.*`，core_builtin 双方都随附了它）。

其余一切仍归 .so 私有。`is_exported` 只被库构建的过滤逻辑消费；普通编译不受影响。

随库源码检测是双保险：一是对文件文本做行首 `#`+字母扫描（`_lib_text_needs_source` —— 经 `string_char_code_at` 逐字符比较，原生 `>=` 字符串比较会错误编译，见 `Tests/StringTest/StringRelationalGeNative.md`）；二是 `upgrade_template_files`，它把任何包含泛型 / 值模板参数 / `#fun` 的文件强制划到这一侧。

## 消费

消费方对符号表中的定义只声明、不定义，方法体则调用 `.so`。有两条路径，由 `--libmeta=` 选择（默认 `direct`）：

- **text**（`--libmeta=text`，调试/对齐路径）—— `read_meta` 把符号表物化为无函数体的声明文本（`fun ...;` —— 绝不会是 `extern fun`，因此 extern→libc 映射永远不会触发），写入伪文件 `<libdecls:<libname>>`；`source` 条目原样成为逐文件的 `SourceInput`。物化出的文件带着 `SourceInput.is_lib` 标记走普通流水线，因此 `is_lib_export` 机制（pass 1 范围标记、pass 8 跳过函数体、IR 声明发射、`check_lib_redefinition`）原样生效。
- **direct**（`--libmeta=direct`，默认）—— `libmeta_direct_inject` 在语义 pass 1 与 pass 2 之间运行，从 JSON 构建预构建的 bound 定义，并把它们拼接到定义列表中（如今已空的）声明槽位上：
  1. **骨架** —— 经 `add_or_merge_namespace` 建立命名空间作用域（作用域与符号同时注册），并创建设置了 `is_lib_export`/`is_lib_source` 的 class/enum/iface/fun/global 外壳；
  2. **签名回填** —— 类型经 `resolve_type_json` 解析（带点号的基本名按段解析；参数**不**预填充 —— pass 4 会镜像它们，双重填充是参数数量错误）、返回类型填充（缺失返回类型会让调用绑定为 void）、采用预构建虚表（`vt` 槽位与实现方法符号配对）、`implfor` 虚表按方法名构建；
  3. **全局初始化器** —— `init` 表达式文本经词法分析、语法分析（`Parser.parse_expression`），再针对该全局变量的作用域完成绑定，且在全部签名存在之后进行（过早绑定会缓存过时的返回类型，例如 `void`）。

  pass 2/6/8 会跳过被注入的范围并重映射 AST 索引（`SemanticModel.libmeta_is_injected` / `libmeta_ast_index`）。

两种模式产出逐字节一致的 `.ll`（`Tests/DynamicLinkTest/LibMetaTextDirectParity.md`）。

exe 侧携带 C 运行时 + 可选的 JIT；库的 `_emperor_*`/`__builtin.*` 引用经 `-rdynamic` 从 exe 绑定，而 `link_exe` 添加 `-rpath,$ORIGIN`，使 exe + `.penguin-lib` 配对可重定位。

## 构建库

`-o X.penguin-lib` 触发库模式：发射器写出 `X.ll`，`link-lib` 构建 `.so`，`serialize_symbols` 附加元数据 + 尾部标记。构建编译器库需要具备 JIT 能力的构建（`-enable-meta`）。库依赖递归解析（`deps[]`，经 `visited` 防环）。

## std 动态库

标准库模块以独立的动态库 `libemperorpenguin-std.penguin-lib` 分发，由自举的 pass3 阶段从 `EmperorPenguin/EmperorPenguinStd.penguins` 构建。`#template`/`#fun` 文件（utils/json/vector/hashmap/array/argparse）按上文保留集规则以**逐字源码**交付；metaconfig 以 export 标记的符号表条目交付。`dynlib.penguin` 本身不在其中——libmeta 构建器/注入器操作编译器的 bound 树，属于编译器模块，仍留在 `EmperorPenguinLib.penguins`。编译器库在 `deps[]` 中登记 std 库，因此每个编译器库的消费方都传递地拉入它。

`main.penguin` 在 `--lib` 链之前自动加载 std 库：当 `std_enabled && dl_enabled && dynlib_available()`（`--enable-std`，默认开；`--disable-std` 关闭）时，探测 `<compiler_exe_dir()>/libemperorpenguin-std.penguin-lib`，若没有 `--lib` 条目已指名该文件则压入库链。文件不存在时（自举各 pass、BabyPenguin VM、Windows 单体构建——`.penguin-lib` 机制仅限 ELF，std 编进单体）探测静默跳过。

`load_lib_recursive` 按**库名**去重（`state.lib_names`），而非按路径：同一个库既可以经显式 `--lib` 路径到达，也可以经 `deps[]` 从引用方库所在目录解析到达，不去重的话双重注入会是重复定义错误。

std 库的 libmeta 把每个程序都会用到的 core_builtin 泛型实例（`Option`、迭代器……）作为已发布实例携带，因此 auto-std 下连 hello-world 都会得到对它的 `DT_NEEDED` 项。为此 `link_exe` 以 `-Wl,--as-needed` 链接（符号全部未被使用的库退出依赖），链接后再对产物执行 `readelf -d`，把每个 `DT_NEEDED` 的 `.penguin-lib` 复制到其旁边——可执行文件 + 复制出的库配对即可原地运行。
