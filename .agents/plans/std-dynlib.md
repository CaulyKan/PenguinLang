# std 拆分为 `libemperorpenguin-std.penguin-lib`

状态：**已完成**（2026-09-19，分支 `feature/std-dynlib`）。

> **实施偏差（2026-09-19，实施时发现）**：
> 1. `dynlib.penguin` **不能**进 std lib——它的 libmeta 构建器/注入器操作编译器 bound 树类型（`BoundCompilationUnit`/`SemanticModel`/`BoundDefinition`…），脱离编译器源码无法编译（首次 `make bootstrap` 在 pass3 std-lib emission 报 725 个 E_RESOLVE_TYPE/E_RESOLVE_SYMBOL 全部位于 dynlib.penguin）。修正：dynlib.penguin 留在 `EmperorPenguinLib.penguins`（加载器本就是编译器的一部分）；std lib = `src/utils.penguin` + metaconfig/json/vector/hashmap/array/argparse。
> 2. 计划未提及的必要补充：`src/utils.penguin` 必须随 std 进 std lib（argparse 引用 `_utils.*`、core_builtin 的 `#specializing` 块以 `_utils.List` 为目标），相应地 `EmperorPenguinLib.penguins` 必须去掉 utils（否则 Lib 构建 --lib std 时双重注入）。utils 作为 `#template` 文件以原文源码随 std lib 分发。

## 0. 目标

1. 把目前编进编译器的 std 模块（metaconfig / json / vector / hashmap / array / dynlib）+ argparse 共 **7 个模块**打包为独立 dynlib `libemperorpenguin-std.penguin-lib`。
2. 新增编译器选项 `--enable-std`（**默认开**）：自动从编译器可执行文件所在目录加载该 dynlib。
3. 自举链改为：**pass3 最后一次直接吃 std 源码**（它一边产出 std lib，一边产出 std-free 的 pass4）；pass4→pass5、release、LSP、tools 之后全部通过 dynlib 消费 std，不再编译 std 源文件。
4. 发布时该 std lib 一同打包。
5. 不再使用 `EMPEROR_PENGUIN_ROOT` 环境变量去找 std。
6. 附带的两个小改动：
   - `emperor_penguin` / `emperor_penguin.bat` 支持 `--help`，打印所有支持的选项（含转发给 llvm emitter 的语义 flag 全表）。
   - `CompilerConfig` 中大部分高级功能默认开启，自举过程中显式关闭，简化用户使用体验。

已与用户确认的两个决策：

- **std lib 内容**：6 个编译器 std 模块 + argparse（共 7 个）。`PenguinTools.penguins` 因此可以去掉 `argparse.penguin` 源码条目。
- **Linux 发布形态**：复用 `EmperorPenguinLib.penguins` + `EmperorPenguinExe.penguins`（与自举 pass4 完全同构），不新增项目文件；部署 3 个文件（driver 脚本 + emitter exe + 两个 `.penguin-lib`，rpath `$ORIGIN`，与现有 LSP/tools 模式一致）。

## 1. 现状分析（探索结论）

### 1.1 std 当前如何进入编译

`EmperorPenguin/main.penguin`：

- L102/L107：`core_builtin.penguin`（1068 行）与 `io.penguin`（354 行）**无条件**以 `_utils.file_read_text(...)` 读取并作为 `SourceInput` 注入；
- L115-118：`scheduler.penguin`（1578 行）仅在 `config.coroutine_enabled` 时注入；
- 其余 std 模块（metaconfig/json/vector/hashmap/array/dynlib/argparse）**不在任何 auto-load 路径**，而是由 `.penguins` 项目文件的 `sources=[...]` 列表显式列出：
  - `EmperorPenguinPass2.penguins`（Full 单体，pass2→pass3 与 Windows 链路）含全部 6 个；
  - `EmperorPenguinLib.penguins`（编译器 as .penguin-lib）含全部 6 个；
  - `EmperorPenguin/tools/PenguinTools.penguins` 单独列了 `../std/penguin/argparse.penguin`。

**结论**：core_builtin / io / scheduler 机制上必须保持"源码自动注入"（每次编译都有，包括 std lib 自己的构建——std lib 里的 json/vector 等要用 `Option`、`StringBuilder` 等；要它们进 lib 会导致自举循环依赖），这一条与用户理解一致，不改。json/vector/hashmap/array/dynlib/metaconfig/argparse 是可拆的部分。

### 1.2 dynlib 机制为什么能承载 std

- **lib 模式触发**：`CompilerConfig.is_lib_mode()` = 输出名以 `.penguin-lib` 结尾（`src/project/CompilerConfig.penguin:90-92`）。
- **libmeta 内容**（`std/penguin/dynlib.penguin:1381-1473` 的 `LibMetaBuilder.build`）：
  - 文本扫描含模板/meta 构造的文件（`#template`/`#fun`/`#specializing`）→ 这些文件以 **原文源码**随 lib 分发（`kind:"source"` 条目），消费方本地重新单态化（json/vector/hashmap/array 正属此类，今天 `libemperorpenguin.penguin-lib` 就是这么分发它们的）；
  - 其余 def 按**符号表**导出（`_lib_def_is_exported`，`std/penguin/dynlib.penguin:1476-1483`），需要显式 `export` 标记；**所有 global 与 `impl for` 边无条件入表**（`build` 第 3 步种子，L1401-1410）。
- **消费方**：`main.penguin` 把 `--lib` 链的每条 lib 的嵌入源码 + 一条 `<libdecls:NAME>` 声明伪文件（`--libmeta=text`）或注入槽位（`--libmeta=direct`，默认）加进 `inputs`，def 顺序与 `.ll` 在两种模式下字节一致。
- **依赖链**：libmeta 有 `deps:[name]`，`load_lib_recursive` 按名解析到「引用方目录 → 编译器 exe 目录 → 当前目录」（`std/penguin/dynlib.penguin:1991-2062`）；`compiler_exe_dir()` 用 `_utils.exe_path()`，Linux 下是 `/proc/self/exe`（`std/c/core_builtin.c:686`），**符号链接 `build/bootstrap/pass4` 也能解析到 pass4.d/ 真实目录**。
- `dl_enabled` 默认 `true`；`dynlib_available()` 在默认项目里返回 `false`（`src/project/DynlibStub.penguin:158`），只有 pass3+（由 `EmperorPenguinPass2.penguins` 构建、含 json 后备 Dynlib）返回 `true`（`std/penguin/dynlib.penguin:2071`）。
- lib 消费 lib（deps 递归、deps-first、去重 `visited`）已被 LSP/tools 验证。

### 1.3 现有自举链（`Makefile:205-348`，linux 分支）

```
pass1: BabyPenguin --backend=cs (EmperorPenguinPass1.penguins) -> $(BS)/pass2.ll -> link -enable-meta -> $(BS)/pass2
pass2: $(BS)/pass2 (EmperorPenguinPass2.penguins, --enable-coroutine) -> pass3.ll -> link -> $(BS)/pass3
pass3: EmperorPenguinLib.penguins -> pass4.d/libemperorpenguin.{ll,libmeta} -> link-lib
       EmperorPenguinExe.penguins --lib libemperorpenguin.penguin-lib -> pass4.d/pass4.ll -> link -enable-meta --consumer-lib -> $(BS)/pass4 (+ symlink)
pass4: 同构 -> $(BS)/pass5.d/... -> $(BS)/pass5 (+ symlink)
bootstrap: md5 比对 (exe4==exe5 && lib4==lib5)，pass5.d 构件保留以便复验
```

`$(EP_STD)`（Makefile:151）= core_builtin + io + scheduler，作为几乎所有阶段的依赖。

### 1.4 release / publish 现状

- `$(REL)/emperor_penguin_llvm_emitter.ll` = **pass4 + `EmperorPenguinPass2.penguins`（Full 单体）** → link 到 `build/linux/emperor_penguin_llvm_emitter`（-enable-meta，L436-462）；win 侧同一 `.ll` 交叉链接（L480-486）。
- `$(REL)/libemperorpenguin.{ll,libmeta}` = pass4 + Lib 项目 → `build/linux/libemperorpenguin.penguin-lib`（**给 LSP/tools 用**，L447-469）。
- `build/linux/emperor_penguin` = 复制 `EmperorPenguin/emperor_penguin` 脚本（L471-473）。
- `publish` 把 `build/linux/*` + `EmperorPenguin/std/{penguin,include,c}` + `EmperorPenguin/src` 拷进 vscode server 目录（L712-771），CI 打 zip 上传。

### 1.5 驱动脚本现状

`EmperorPenguin/emperor_penguin`（429 行 bash）三种模式：full（默认）/ `link` / `link-lib`；`parse_link_opts`（L306-335）+ full 的内联循环（L361-390）。**目前没有任何 `--help`/`-h` 处理**；`EMPEROR_PENGUIN_ROOT` 在 L24/38-45 用于定位含 `EmperorPenguin/std` 的树。`.bat`（336 行）是镜像实现，L26/30 同样用该环境变量，选项解析从 L56 起。

### 1.6 测试基础设施

`Tests/PenguinTestRunner` 的后端：`babypenguin`（dotnet）、`pass1`、`pass2`（`build/bootstrap/pass2`）、`pass3`（`build/bootstrap/pass3`）、`prebuilt`（用户显式给 exe 路径）。**没有 pass4 后端**；现有 StdlibTest 用例以 `Compile.Args: EmperorPenguin/std/penguin/xxx.penguin` 传 std 源码，`Apply To` 是 Pass2/Pass3。pass2/pass3 二进制旁边没有 std lib（pass3 在 `build/bootstrap/`，其 exe 是单体，且 `cfg.std_enabled` 默认开也探测不到文件）→ auto-std 无感跳过，**现有测试零改动**。

## 2. 设计

### 2.1 新的自举/发布链（linux）

```
pass1  BabyPenguin --backend=cs (Pass1 项目)                       [--disable-dl --disable-coroutine --disable-std]
pass2  $(BS)/pass2  (Pass2 项目 = Full 单体, 含 std 源码)           [--enable-coroutine --disable-std]
pass3  $(BS)/pass3  (Pass2 项目 = Full 单体, 含 std 源码, 最后一次吃 std 源码)
        ├─ 构建 std lib:  pass3 + EmperorPenguinStd.penguins -o pass4.d/libemperorpenguin-std.penguin-lib
        │                 [--disable-coroutine --disable-std]  -> link-lib -> pass4.d/libemperorpenguin-std.penguin-lib
        └─ 构建 pass4 lib: pass3 + EmperorPenguinLib.penguins   (已去 std)
                          [--disable-coroutine --disable-std --lib pass4.d/libemperorpenguin-std.penguin-lib]
                          -> pass4.d/libemperorpenguin.{ll,libmeta} -> link-lib
          构建 pass4 exe: pass4(exe) 之前: pass3 + EmperorPenguinExe.penguins
                          [--disable-coroutine --disable-std --lib pass4.d/libemperorpenguin.penguin-lib]
                          -> pass4.d/pass4.ll -> link -enable-meta --consumer-lib <compiler lib>  (std lib 经 .libs 闭包)
pass5  同构；另加 cp pass4.d/libemperorpenguin-std.penguin-lib -> pass5.d/（运行期按名解析 + 链接需要）
bootstrap md5 收敛：exe4==exe5 && lib4==lib5（std lib 两轮共用同一文件，属固定输入）
```

要点：

- **Makefile 驱动的编译一律显式 `--disable-std` + 显式 `--lib`**，不依赖 auto-std，保证任何 cwd / 任何旁置文件下都确定性可重现。
- pass4/pass5 两轮输入完全一致（同一 std lib 文件、同一 flags）→ md5 收敛条件不变。
- std lib 只在 pass3 阶段构建一次（pass3 是"最后一次能直接吃 std 源码且具备 dynlib 能力"的编译器：pass2 无 dynlib 能力，pass3 有），pass4/pass5 只复制。

### 2.2 `--enable-std` 语义

- `CompilerConfig.std_enabled: bool = true`，解析 `--enable-std` / `--disable-std`（含单横线形式）。
- `main.penguin` 在加载 `--lib` 链**之前**：当 `std_enabled && dl_enabled && dynlib_available()` 时，探测 `<compiler_exe_dir()>/libemperorpenguin-std.penguin-lib`：
  - 存在且未被 `--lib` 显式指定 → push 进 `lib_paths`（verbose>=1 打一行日志）；
  - 不存在 → **静默跳过**（这就是 pass1/pass2/dotnet VM/`build/bootstrap/pass3` 等无 std lib 场景的无感路径）。
- 关闭方式：`--disable-std`。

### 2.3 release 形态（linux）

```
build/linux/
├── emperor_penguin                      (脚本, 从 EmperorPenguin/ 复制)
├── emperor_penguin_llvm_emitter         (薄 exe: main.penguin + 链接 libemperorpenguin)
├── libemperorpenguin.penguin-lib        (编译器 .so, 已去 std; deps=[libemperorpenguin-std])
├── libemperorpenguin-std.penguin-lib    (std .so)
└── *.sh
```

- `$(REL)/libemperorpenguin.{ll,libmeta}` 改为 **pass4 + Lib 项目（显式 --lib std lib）** 产出 → link-lib 到 `build/linux/`；再 `cp` std lib 到 `build/linux/`。
- 新规则 `$(REL)/emperor_penguin_exe.ll` = pass4 + `EmperorPenguinExe.penguins --disable-coroutine --disable-std --lib build/linux/libemperorpenguin.penguin-lib`；`build/linux/emperor_penguin_llvm_emitter` 改为链接它。`.libs` 闭包（由 emitter 写出，含 deps 链上的两个 lib）驱动 link；必要时追加 `--consumer-lib` 保险。
- 旧 `$(REL)/emperor_penguin_llvm_emitter.ll`（Full 单体）规则**保留**，仅供 `release_win` 使用。

### 2.4 Windows

`.penguin-lib` 机制是 ELF 专属（SONAME / `$ORIGIN` rpath / `-rdynamic` 符号插入），Windows 自举与发布**保持 Full 单体、std 源码编在内**。win 上 `--enable-std` 因旁边没有 std lib 而自动 no-op；`release_win` 的 emitter 继续用 Full 单体 `.ll`。这个平台不对称要写进文档。

## 3. 文件改动清单

### 3.1 `EmperorPenguin/src/project/CompilerConfig.penguin`

- `coroutine_enabled: bool = false` → `true`（注释更新：高级功能默认开，自举/特例场景显式 `--disable-coroutine`）。
- 新增解析 `--disable-coroutine` / `-disable-coroutine`。
- 新增 `std_enabled: bool = true` + `--enable-std` / `-enable-std` / `--disable-std` / `-disable-std`。
- `dl_enabled` 已默认 true、`libmeta_mode` 已默认 direct，不动。
- `enable_meta` 保持 opt-in（链接期重量级选项，不属于"语义高级功能"）。
- 注释里写清"所有 Makefile 驱动的编译显式传 flag"的约定。

### 3.2 `EmperorPenguin/main.penguin`

- 在 `--lib` 链加载之前插入 auto-std 探测（用 `compiler_exe_dir()`）。
- bare-usage 文案（L12-13）更新，提 `--enable-std` 与 `--help`（emitter 自身的 `--help` 也可考虑；见 3.7）。
- 现有"no source files specified"分支保持（`--help` 由驱动脚本处理，不进 emitter；但 emitter 直接收到 `--help` 时不宜报错——在 3.7 一并处理）。

### 3.3 `EmperorPenguin/std/penguin/dynlib.penguin`

- `load_lib_recursive` 增加**按库名去重**：读完 `meta` 后若 `meta.name` 已在 `state.lib_names` 中则直接返回（同一 lib 可能经"显式 `--lib`（绝对路径）"与"dep 按名解析（引用方目录）"得到两个不同路径串，不去重会双重注入 → 重复定义）。
- （可选）`read_meta` 对同一文件做路径规范化去重，与上面二者取一即可，优先做库名去重。

### 3.4 `EmperorPenguin/std/penguin/metaconfig.penguin`

- `fun meta_runtime_available` 加 `export`（非模板文件必须显式 export 才会进符号表供消费方使用；现在它靠"编在同一编译单元里"才可见）。

### 3.5 新项目文件 `EmperorPenguin/EmperorPenguinStd.penguins`

```
[Project]
name="EmperorPenguinStd"
sources=["std/penguin/metaconfig.penguin", "std/penguin/json.penguin", "std/penguin/vector.penguin",
         "std/penguin/hashmap.penguin", "std/penguin/array.penguin", "std/penguin/dynlib.penguin",
         "std/penguin/argparse.penguin"]
```

（顺序沿用 Pass2 清单，argparse 追加；无 `main.penguin`，lib 模式由 `-o *.penguin-lib` 触发。）

注意：`EmperorPenguinPass1.penguins` 的源集不变（它不含 std）；Pass1 构建时用 `--disable-std`。

### 3.6 源集瘦身

- `EmperorPenguinLib.penguins`：删去 6 个 std 条目；注释更新为"std 来自 `--lib libemperorpenguin-std.penguin-lib`（deps 记录）"。
- `EmperorPenguin/tools/PenguinTools.penguins`：删去 `../std/penguin/argparse.penguin`（经 libemperorpenguin 的 dep 链传递获得）。
- `EmperorPenguinPass2.penguins` **不动**（pass2→pass3 + Windows 全链路的 Full 单体）。
- `MagellanicPenguin/LspServer/LspServer.penguins` **不动**（LSP linux 已经只列自己的模块 + 经 lib 消费；LspServerWin 仍编 std 源码）。

### 3.7 `EmperorPenguin/emperor_penguin` + `.bat`

- 新增 `-h|--help|help`：在模式分发**之前**处理，打印：
  - 三种模式（full / `link` / `link-lib`）与各自的位置参数；
  - 脚本级选项：`-o/--output`、`-enable-meta`、`-target=`、`-llvm-win`、`--consumer-lib`、`--emitter`、`--lib`；
  - **转发给 emitter 的语义 flag 全表**：`-v/-vv/-vvv`、`--enable-coroutine/--disable-coroutine`、`--enable-dl/--disable-dl`、`--libmeta=text|direct`、`--define A=B`、`--meta-src <file>`、`--enable-std/--disable-std`；
  - 环境变量：`EMPEROR_EMITTER`、`CLANG`、`LLVM_CONFIG`、`OPT`、`MINGW_PREFIX` / `WIN_CC` / `WIN_CXX` / `WIN_AR` / `WIN_CLANG`、`LLVM_WIN_PREFIX`；
  - exit 0。
- 删除 `EMPEROR_PENGUIN_ROOT` 探测分支（保留 SCRIPT_DIR、`$SCRIPT_DIR/EmperorPenguin/std`、`$SCRIPT_DIR/../EmperorPenguin/std` 两跳）；die 文案不再提该变量。
- `.bat` 同步：`:help` 段 + 删除环境变量分支。
- emitter 自身（`main.penguin`）收到 `--help` 时打印简表并 exit 0（避免被当成 source file 报错）。

### 3.8 Makefile（linux 分支）

- 新变量 `EPSTD_SRC := EmperorPenguin/EmperorPenguinStd.penguins + 7 个 std 文件`。
- 新规则（pass3 产出 std lib）：

  ```make
  $(BS)/pass4.d/libemperorpenguin-std.ll $(BS)/pass4.d/libemperorpenguin-std.libmeta &: $(BS)/pass3 $(EPSTD_SRC) $(EP_STD)
      $(BS)/pass3 EmperorPenguin/EmperorPenguinStd.penguins $(VERBOSE) --disable-coroutine --disable-std \
          -o $(BS)/pass4.d/libemperorpenguin-std.penguin-lib ...
  $(BS)/pass4.d/libemperorpenguin-std.penguin-lib: <.ll> <.libmeta> $(C_RT)
      EmperorPenguin/emperor_penguin link-lib ... -o $(BS)/pass4.d/libemperorpenguin-std.penguin-lib
  ```

- pass4 lib/exe、pass5 lib/exe 规则：加 `--disable-coroutine --disable-std --lib <std lib>`；pass5 加 `cp` std lib。
- pass1：加 `--disable-coroutine --disable-std`（BabyPenguin VM 无法绑定 scheduler 外部符号，且 pass1 无 dynlib 能力）。pass2→pass3：加 `--disable-std`。
- `bootstrap` 收敛检查不变（exe+lib md5）。
- release：见 §2.3；`release_win` 不动。
- LSP/tools linux 规则：flags 加 `--disable-std`，依赖加 std lib 文件；LSP/tools 链接用 `--consumer-lib`（现有）或由 `.libs` 闭包带上 std lib。
- `publish`（linux 段）：`cp build/linux/libemperorpenguin-std.penguin-lib MagellanicPenguin/vscode/server/linux/`。

### 3.9 `Tests/Readme.md`

- 注明：`Compile.Args` 传 std 源文件仍是 Pass1/2/3 后端的要求（这些后端旁边没有 std lib，auto-std 自动 no-op）；发布版编译器（pass4+ / `build/linux/emperor_penguin`）用 `--enable-std` 默认获得 std，无需 Compile.Args。

### 3.10 文档

- `docs/en/tutorials/CompilerUsage.md` + `docs/zh/tutorials/CompilerUsage.md`：
  - 删 `EMPEROR_PENGUIN_ROOT` 相关（L75 环境变量表、L117/L121 "repo 外调用"段）；
  - 补 `--help`、`--enable-std/--disable-std`、新默认值（coroutine 默认开）；
  - 更新发布布局（emitter = exe + 2 个 `.penguin-lib`）与 Windows 不对称说明。
- `AGENTS.md`：自举布局（std lib 步骤、pass4.d 内容变化）、stdlib 表（std 模块成为 dynlib、argparse 归位）、CompilerConfig flag 默认值说明。
- `README.md` / `README.zh-CN.md`：如有相关描述一并更新。

## 4. 实施顺序与验证

0. 按仓库工作流：建分支 `feature/std-dynlib`；计划归档到 `.agents/plans/std-dynlib.md`（本文件）；里程碑分别提交。
1. 编译器改动（§3.1 CompilerConfig、§3.2 main、§3.3 dynlib、§3.4 metaconfig）→ `dotnet build && dotnet test`（in-process 用例全绿）。
2. 项目文件（§3.5 新增 Std、§3.6 瘦身 Lib/Tools）→ Makefile 自举链（§3.8）→ `make clean && make bootstrap` 直至 pass4/pass5 收敛（exe+lib md5 一致）。
3. release/lsp/tools：`make release lsp tools tools-test`。
4. `make test`：默认 `Apply To` 的 babypenguin/pass2/pass3 必须全绿（现有用例零改动）。
5. 手工冒烟：
   - 异目录 cwd 下用 `build/linux/emperor_penguin` 编译使用 `std.Vector` / `std.parse_args` 的程序（不带任何 `Compile.Args`）→ 验证 auto-std；
   - `--disable-std` 关闭路径（应报未定义符号）；
   - `emperor_penguin --help`；
   - `make publish`（linux 冒烟：脚本冒烟 + LSP 冒烟已覆盖 3 个文件的部署布局）。
6. 遇到可复现的编译器 bug → 按仓库规约固化为 `Tests/<Category>/<Name>.md`（红/绿回归哨兵）。

## 5. 风险与对策

| 风险 | 对策 |
| --- | --- |
| libmeta direct 注入承载编译器本体（dynlib/metaconfig 走符号表） | json/vector/hashmap/array 走原文源码路径（与现 libemperorpenguin 相同，已验证）；符号表注入失败时 `--libmeta=text` 是对拍/调试回退路径 |
| pass4/pass5 不收敛 | 两轮使用完全相同的显式 flags 与同一 std lib 文件（输入字节一致）；库名去重防止"显式 --lib + deps 解析"双路径双注入 |
| auto-std 默认开导致用户编译变慢（约 5k 行 std 源码解析+绑定，类比现有 core_builtin 1068 行自动加载） | `--disable-std` 可关；若测试显示明显回归，再评估惰性方案（本次不做） |
| StdlibTest 传 std 源文件的用例 | `Apply To` 是 Pass2/Pass3，后端从仓库源路径取 std，行为不变；将来 runner 增加 pass4/发布版后端时再迁移到 `--enable-std` |
| Windows 无法用 dynlib（ELF 专属） | 文档明确平台不对称；win 上 `--enable-std` 自动 no-op；win 自举/发布保持 Full 单体 |
| `metaconfig.penguin` 加 export 后行为变化 | 仅扩大可见性（此前靠同编译单元可见）；确认 pass3 构建 std lib 时 meta 门正常 |

## 6. 待办清单（实施时勾选）

- [x] 分支 `feature/std-dynlib`
- [x] CompilerConfig：coroutine 默认开 + `--disable-coroutine`；`std_enabled` + `--enable-std/--disable-std`
- [x] main.penguin：auto-std 探测 + usage 文案 + `--help`
- [x] dynlib.penguin：库名去重
- [x] metaconfig.penguin：`export`
- [x] 新增 `EmperorPenguinStd.penguins`（偏差：不含 dynlib.penguin，含 utils.penguin）
- [x] `EmperorPenguinLib.penguins` 去 std（dynlib.penguin 留下）；`PenguinTools.penguins` 去 argparse
- [x] Makefile linux：std lib 规则、pass1..pass5 flags、release lib+exe、lsp/tools、publish（win 链也补了显式 flags）
- [x] `emperor_penguin` + `.bat`：`--help`、删 `EMPEROR_PENGUIN_ROOT`
- [x] `dotnet test` 绿（BabyPenguin 56 + EmperorPenguin 473）
- [x] `make bootstrap` 收敛（exe+lib md5 一致）
- [x] `make release lsp tools tools-test`
- [x] `make test` 全绿（1575 PASS / 0 FAIL / 217 SKIP；CoroutineSyntaxRequiresFlag 按 flag 默认翻转改为显式 `--disable-coroutine`，LspTest/SelfHostProjectModeLibChain 的 fixture 陈旧 lib 路径修复为 build/linux/）
- [x] 冒烟：auto-std（std.Vector 程序 sum=42）/ `--disable-std` 报错 / emitter+driver `--help` / staged 发布布局 parse_args 三路径（默认/传参/--help）全过
- [x] 文档：CompilerUsage(en/zh)、CLAUDE.md(=AGENTS.md)、Tests/Readme.md（README 无需改动）
- [x] `make publish`（含自带冒烟；脚本链接改为 `-Wl,--as-needed` + 链接后把 DT_NEEDED 的 lib 自动复制到产物旁——auto-std 下连 hello world 都运行期依赖 std lib，因其 libmeta 以"已发布实例"形式承载所有程序都用的 core_builtin 泛型实例）

## 7. 实施中追加的修复（计划外发现）

1. **`--as-needed` + 伴生 lib 自动复制**（见上）：否则 auto-std 让所有用户产物都硬依赖部署目录中的 std .so。
2. **LspTest/SelfHostProjectModeLibChain 的 fixture 陈旧路径**：`.magellanic.config` 指向 8 月 Makefile 重构前的 `build/libemperorpenguin.penguin-lib`，改为 `build/linux/`（该测试在 master 上本就红；修复后 15.9s 通过，恰好端到端验证新 lib 对的 LSP 消费路径）。
3. **PortTest/CoroutineSyntaxRequiresFlag**：flag 默认翻转后改为显式 `--disable-coroutine` 触发同一 gate 报错。
4. **Tests/StdlibTest/ArgparseKeywordAnnotationHangs.md**：keyword 风格 `#arg(help: ...)` 注解使编译器死循环（100% CPU 无诊断）——红哨兵，修好后自动转绿。
