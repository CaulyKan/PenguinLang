# 32. 测试框架

PenguinLang 让每个编译器互相验证。基础设施分两半：**跨编译器端到端套件**（每例一个 markdown 文件，由单文件控制台运行器驱动真实编译器进程）与针对编译器内部的**进程内 xunit 单元测试**。根 Makefile 编排构建、自举、测试与文档站。

## Markdown 测试套件（`Tests/`）

33 个类别目录、约 614 例（`Tests/<Category>/<Name>.md`），由 `Tests/PenguinTestRunner.csproj` 驱动——一个普通 `net10.0` 控制台 exe，全部实现是 **`Tests/Program.cs`（2838 行，无测试 SDK）**。格式参考：`Tests/Readme.md`。

### 用例格式

```markdown
# Test Name
## Description       （可选）
## Apply To          （必需：BabyPenguin / BabyPenguin CS /
                      EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS) /
                      EmperorPenguin Pass2 / EmperorPenguin Pass3 / Prebuilt）
## Test Code         （必需的 ```penguin 围栏块）
## Compile           （Args / Env / Stdin / ExpectedExitCode / ExpectedStdout / ExpectedStderr）
## Run               （可选；负向编译测试省略）
## Build N           （多阶段构建，仅 Pass2/3；每阶段有自己的 Test Code）
## Run LSP           （仅 Prebuilt 的 LSP 会话）
## Skip              （无条件跳过）
```

* 流期望：`EQUALS`（逐字节）、`ESCAPE`（C 转义后比较）、`MATCH`（带 `\d`/`\s`/`\w`/`.*` 洞的近似字面锚定模式——用于 LSP `Content-Length` 计数）、`CONTAINS`、`DISCARD`。
* 退出期望：整数、`NONZERO` 或 `ANY`。
* `ExpectedStdout: EQUALS \`...\`` 字面量可跨行到闭合反引号；`${VAR}` 环境展开作用于 Args/Env/Stdin 与期望（`${PENGUIN_ROOT}`、`${WORKDIR}`）。
* `BabyPenguin` 以单进程编译+运行——一个退出码同时对照两个阶段；`Compile.Args` 被忽略（总是 `-q`）。Emperor 后端遵守 `Compile.Args`（如 `--enable-coroutine`、标准库文件）。

### 运行器架构（`Tests/Program.cs`）

* `Main`（26 行）：解析选项 → 定位仓库根（`LocateRepoRoot`，305；设 `PENGUIN_ROOT`）→ 发现/解析 `.md` → 构建工作项（测试 × 编译器）→ `BootstrapGuard.Check`（114；缺 `build/bootstrap/pass2|pass3` 时**退出 2**——运行器从不自举）→ 缺失时自动构建 BabyPenguin Release DLL（失败**退出 3**）→ 创建 `build/testruns/<timestamp>/` → 加载基线 → 对测试组 `Parallel.ForEachAsync`（171）。退出码：0 全过，1 有失败/错误，2 自举守卫，3 BabyPenguin 构建失败。
* **编译器**（`BuildBackends`，349；`ICompilerBackend`，1392）：`BabyPenguinBackend`（`dotnet BabyPenguin.dll -q <src>`，解释执行，1455 行）、`BabyPenguinCsBackend`（`--backend=cs`，1480）、`EmperorOnVmBackend`（Pass1：`dotnet BabyPenguin.dll -q EmperorPenguinPass1.penguins -- <args> <src>`，1504）、`EmperorNativeBackend`（Pass2/3：`build/bootstrap/passN <args> <src>`，1538）、`PrebuiltExeBackend`（复制 exe 及 rpath 所需的相邻 `.penguin-lib`，1579）。
* **链接**（`EmperorLink`，1414）：Emperor 后端只输出 `.ll`；运行器经 `emperor` 脚本链接——exe 用 `emperor link <out>.ll -o <out>`（消费库闭包来自 `<out>.libs` 附属文件），库用 `emperor link-lib <base>.ll <base>.libmeta -o <out>.penguin-lib`。
* **产物**：每组合工作目录 `build/testruns/<ts>/<compiler>/<category>/<test>/`，含 `source.penguin`、`out.exe`/`combined.ll`、`compile.log`、`run.log`、`result.json`。每个被派生的阶段进程都获得 `TMPDIR = 工作目录`，emperor 脚本的临时产物落在组合目录里。
* **指标**：每阶段时长（Stopwatch）与**每 40ms 轮询 `/proc/<pid>/status` VmHWM 的峰值 RSS**（`ReadVmHwm`，1371）。
* **MemGate**（399–551）：派生前按估计峰值 RSS 的 FIFO 准入控制（pass2/3 元编译为 Heavy 5 GiB、Light 512 MiB、1.5 GiB `MemAvailable` 活下限、15 分钟放弃）——防止过度并行下 OOM 杀手（退出码 137）；也限制 `--parallel`。
* **基线**：`build/testruns/latest.json`（默认对比）、`--compare-with none|latest|<path>`；`--baseline` 把本次运行记录为新基线（普通运行绝不覆盖它）。差异标记新失败/通过/跳过与时间/内存回归（`--time-regression-pct`、`--mem-regression-pct`，默认都 50%）。
* **报告**：`SummaryReporter.WriteHtml`（2301）——自包含的 `summary.html`（统计卡、每编译器通过率、可过滤表格、每阶段详情浮层、暗色模式）；`WriteJson`（2771）写 `summary.json`。
* 选项（574–627）：`--compilers`、`--filter <glob|substr>`、`--probe`（忽略 Apply To）、`--parallel <n>`（默认核数−1）、`--timeout-compile 600`、`--timeout-run 60`、`--env KEY=VAL`（如 `EMPEROR_GC_MODE=greentea`）、`--baseline`、回归阈值。

### 进程内单元测试

`BabyPenguin.Tests` / `EmperorPenguin.Tests`（xunit 2.9.3，`net10.0`）：对 C# 编译器 API 的直接测试（DeclarationTests 1222 行、Project/Mutability/Complex 测试、`CSharpBackendBenchTest`），以及——EmperorPenguin 侧——**批量测试装置**（`EmperorPenguin.Tests/BatchCompiler.cs`，548 行）：每个 `[Fact]` 带批量特性（`BatchTest`/`BatchBoundTest`/`BatchIRTest`/`BatchLLVMTest`/`BatchParseTest`/`BatchTokenizeTest`）；静态 `Lazy<BatchResults>` 把一个类的全部用例**作为一个程序**在进程内 `BabyPenguinVM` 上编译（每个用例包进 `namespace __test_<name>`、`println` 加 `@@name@@` 标签；EmperorPenguin 编译器自己的 `.penguin` 源作为源目录加载），运行后按标签拆分回各测试。断言是对完整期望文本的 xunit `Assert.Equal`（实际值也转储到 `/tmp/actuals/`）。遗留的 `BatchE2ETest`（进程派生）已无人使用。

## 构建编排——根 Makefile

所有产物位于 `build/`（gitignored）；每个阶段都是带文件级依赖的**文件目标**（`.penguins` 源集经 sed 提取、C 运行时源、驱动脚本）——未变化的输入绝不重编。头部块 `Makefile:1-73` 记录所有目标。

* `make bootstrap`——自举链：pass1（BabyPenguin `--backend=cs` 编 `EmperorPenguinPass1.penguins`）→ `pass2`（带 `-enable-meta` 链接）→ `pass3`（pass2 带 `--enable-coroutine` 编完整 `EmperorPenguinPass2.penguins`）→ 库+exe 对形式的 **pass4/pass5**（`build/bootstrap/pass4.d/`、`pass5.d/`），并做 **md5 收敛检查**（pass4 exe == pass5 exe 且 lib4 == lib5，否则退出 1）。产物保留，重复自举只重新校验 md5。
* `make release`——一次平台无关输出（`build/release/*.ll`）按平台重链：`release_linux`（`build/linux/emperor_penguin` + 发射器 + `libemperorpenguin.penguin-lib`）、`release_win`（`-target=win64` 交叉链接）。
* `make lsp` / `make tools`——语言服务器与 `penguin-tools`（demangle/mangle/meta/format），与 release 库链接；`make tools-test` 跑 `EmperorPenguin/tools/selftest.sh`。
* `make test` / `make baseline_test`——markdown 套件（`TEST_ARGS` 透传）；`make unittest`——`dotnet test`；`make publish`——双平台 + vscode 扩展打包 + 冒烟测试（捆绑 emperor 编译+链接+运行、捆绑 LSP 会话、wine 跑 Windows LSP）；`make docs-site`——本站；`make gc-bench`——GC 基准。

### emperor 驱动脚本（`EmperorPenguin/emperor`，428 行 bash）

编译器只输出 IR；该脚本负责 LLVM 工具链工作：

* `emperor [--emitter <path>] <src...> [flags] -o <out>`——全流程（编译 → 经 `make -C EmperorPenguin/std/c OUTPUT_DIR=<tmp>` 构建 C 运行时 → clang 链接）。`-o *.penguin-lib` 触发库模式；`--lib <x.penguin-lib>` 既转发给发射器又作为消费库链接。
* `emperor link <file.ll> -o <out> [-enable-meta] [-target=win64] [--consumer-lib <so>]...`
* `emperor link-lib <file.ll> <file.libmeta> -o <out>.penguin-lib`——链接 `.so`、SONAME 盖输出 basename、追加元数据 + `PENGUINLIB:<offset>:<size>` 页脚。
* Linux→win 交叉经 llvm-mingw（默认 `/opt/llvm-mingw`，`MINGW_PREFIX`/`WIN_*` 可覆盖）；`-enable-meta` 需要 `llvm-config`（ORC/core 库）并追加 `--whole-archive libpenguin_jit.a` + `-rdynamic`；消费库得到 `-rpath $ORIGIN`；按链接器风格加 32 MB 栈 flag；输入 `.ll` 复制为 `combined.ll` 使相同 IR 链出相同二进制（md5 自举收敛）。

## CI（`.github/workflows/dotnet.yml`）

* **build**（ubuntu-latest）：setup-dotnet → 安装 LLVM 22（apt.llvm.org；符号链接 `clang`/`llvm-ar`/`llvm-config`）→ `dotnet build` → `make bootstrap` → `make lsp tools` → `make unittest` → `make test` → 上传 `summary.html` 产物（总是）。
* **deploy-pages**（push 时）：mdbook 0.4.52 → `make docs-site` → 英文书发布在站点根、中文书在 `/zh/`、最新测试报告在 `/test-report/`。
