# 编译器使用

本页介绍 PenguinLang 工具链：组件构成、开发环境搭建、如何构建自举编译器，以及如何用原生编译器编译并运行程序。

## 组件

| 组件 | 语言 | 职责 |
|---|---|---|
| **BabyPenguin** | C# | 参考编译器与解释器（VM）。直接运行 `.penguin`；也可输出 C#（`--backend=cs`）。用来构建 EmperorPenguin。 |
| **PenguinLangParser** | C#/ANTLR4 | BabyPenguin 使用的语法与解析器库。 |
| **EmperorPenguin** | PenguinLang | 自举编译器。用 PenguinLang 编写、编译自身，输出 LLVM IR（`.ll`）；链接由 `emperor` 脚本驱动。 |
| **MagellanicPenguin** | PenguinLang/C#/TypeScript | 语言服务器（LSP）、调试适配器（DAP）与 VSCode 扩展。 |
| **penguin-tools** | PenguinLang | 命令行工具：`demangle` / `mangle` / `meta` / `format`。 |

自举关系是关键：BabyPenguin（C#）把 EmperorPenguin 的源码编译成原生编译器；此后 EmperorPenguin 编译它自己。任何 EmperorPenguin 编译出的程序都是 LLVM IR 文本；bash/bat 驱动脚本（`EmperorPenguin/emperor`）负责构建 C 运行时并调用 `clang` 链接出可执行文件。

## 前置条件

* **.NET SDK 10**——BabyPenguin、测试运行器与单元测试都面向 `net10.0`。
* **LLVM/clang 22 及以上**——`clang`、`llvm-ar`、`llvm-config` 需在 `PATH` 上。EmperorPenguin 输出 LLVM 22 IR（其调试记录形式要求 clang ≥ 22）；带 `-enable-meta`（JIT 运行时）链接时需要 `llvm-config`。
* **make + bash**——C 运行时构建与 `emperor` 驱动脚本。
* 可选：`make docs-site` 需要 **mdbook 0.4.52**；VSCode 扩展打包需要 **npm**；Linux 上的 Windows 发布冒烟测试需要 **wine**；交叉编译 Windows 二进制需要 **llvm-mingw** 工具链（默认 `/opt/llvm-mingw`）。

## 用 BabyPenguin（解释器）运行程序

最快的循环是 BabyPenguin 解释器，不需要原生工具链：

```bash
dotnet run --project BabyPenguin -- Examples/HelloWorld.penguin
# 或构建后：
dotnet BabyPenguin/bin/Release/net10.0/BabyPenguin.dll -q hello.penguin
```

`-q` 抑制编译器跟踪输出，控制台只显示程序自身输出。BabyPenguin 一步完成编译与运行；它也是测试套件对比原生编译器的参考实现。

## 构建原生编译器（自举）

```bash
make bootstrap
```

这会在 `build/bootstrap/` 下产出自举链：

1. **pass1**——不保留为二进制；BabyPenguin 的 C# 后端把 `EmperorPenguinPass1.penguins`（标准库中不含 `#` 元构造的 EmperorPenguin）直接编译为 LLVM IR。
2. **pass2**——该 IR 经 `-enable-meta`（支持 JIT）链接为原生编译器。
3. **pass3**——pass2 编译 `EmperorPenguinPass2.penguins`（带元编程的完整编译器），得到第一个完整能力的原生编译器。
4. **pass4 / pass5**——pass3 把编译器重建为共享库（`libemperorpenguin.penguin-lib`）加一个小可执行文件；pass5 重复构建，Makefile 校验 **pass4 与 pass5 的 md5 收敛**——编译器逐字节复现自身。

Makefile 的每个阶段都是带文件级依赖的文件目标，因此未变化的目录树不会重编——重复 `make bootstrap` 只重新校验 md5。直接使用产出的编译器：

```bash
build/bootstrap/pass2 file.penguin -o out        # 输出 out.ll
build/bootstrap/pass3 file.penguin -o out        # 输出 out.ll
```

编译器**只输出 LLVM IR**（`out.ll` 及附属文件），从不链接。

## emperor 驱动脚本

`EmperorPenguin/emperor`（bash；Windows 上为 `emperor.bat`）检查 LLVM 环境、构建 C 运行时（`make -C EmperorPenguin/std/c`）并驱动 clang：

```bash
# 全流程：编译 + 构建 C 运行时 + 链接，一条命令
./build/linux/emperor hello.penguin -o hello
./hello

# 或分两步（测试运行器就是这样做的）
build/bootstrap/pass3 hello.penguin -o hello.ll-out
./build/linux/emperor link hello.ll-out.ll -o hello

# 构建共享库而不是可执行文件
./build/linux/emperor link-lib foo.ll foo.libmeta -o foo.penguin-lib
```

常用 flag：`-enable-meta` 链接 LLVM ORC JIT 运行时（由含 `#fun` 源码构建的编译器需要）；`--enable-coroutine` 启用 async/wait 语言支持；`-target=win64` 交叉链接 Windows 二进制（Linux 宿主，经 llvm-mingw）；`--lib <dir>/x.penguin-lib` 以共享库为编译目标；`--emitter <path>` 覆盖编译器二进制。环境变量：`EMPEROR_PENGUIN_ROOT`、`CLANG`、`LLVM_CONFIG`、`OPT`、`MINGW_PREFIX`。

输出的 `.ll` 与平台无关：一次输出可以链接为 Linux、Windows 或 `.penguin-lib`。

## 其他 Makefile 目标

| 目标 | 产物 |
|---|---|
| `make release` | `build/linux/emperor_penguin_llvm_emitter`（编译器）、`build/linux/libemperorpenguin.penguin-lib`（库形态的编译器）、`build/linux/emperor_penguin`（驱动脚本）。`make release_win` 交叉构建 Windows 一对。 |
| `make lsp` | `build/linux/penguin-lsp`——语言服务器，与 release 库链接。 |
| `make tools` | `build/linux/penguin-tools`——`demangle` / `mangle` / `meta` / `format`。`make tools-test` 跑其金测试。 |
| `make test` | 跨编译器 markdown 测试套件（`Tests/*.md`，约 600 例）。快速循环：`dotnet run --project Tests/PenguinTestRunner -- --compilers babypenguin`。 |
| `make unittest` | 对 xunit 工程跑 `dotnet test`。 |
| `make publish` | 把 release + LSP + 工具部署进 VSCode 扩展，发布自包含 .NET 宿主，打包 `.vsix`。 |
| `make docs-site` | 本文档站（英文 + 中文两本书）。 |
| `make gc-bench` | GC 基准测试。 |
| `make clean` | 删除 `build/`。 |

## 原生编译 Hello World——完整走一遍

假设仓库位于 `~/penguinlang`：

```bash
cd ~/penguinlang
make bootstrap                      # 一次；产出 build/bootstrap/pass2、pass3……
make release                        # 可选：把驱动安装为 build/linux/emperor_penguin

cat > hello.penguin <<'EOF'
initial {
	println("hello world from penguin-lang!");
}
EOF

# 一条命令完成全流程：
./build/linux/emperor_penguin hello.penguin -o hello
./hello                             # hello world from penguin-lang!
```

或用自举编译器加显式链接：

```bash
build/bootstrap/pass3 hello.penguin -o hello.exe      # 输出 hello.exe.ll
EMPEROR_PENGUIN_ROOT=$PWD ./build/linux/emperor link hello.exe.ll -o hello
./hello
```

只有从仓库目录之外调用驱动脚本时才需要 `EMPEROR_PENGUIN_ROOT`（它用于定位 C 运行时所需的 `EmperorPenguin/std`）；`make release` 的副本会自行解析自身位置。

## VSCode 扩展与语言服务器

安装 `PenguinLang` 扩展（由 `make publish` 打包，或打开 `MagellanicPenguin/vscode` 运行 `npm run package`）。保存 `.penguin` 文件、按 F5、选择 *PenguinLang Debug* 配置，扩展会启动内置语言服务器编译并运行程序，支持调试（经 DAP 的断点、单步、变量查看）。服务器通过工作区的 `.magellanic.config` 文件识别 `.penguins` 工程文件。

## 工程文件

单文件可直接编译。更大的程序使用 `.penguins` 工程文件（INI 格式）：

```ini
[Project]
name="MyPenguin"
sources=[
	"a.penguin",
	"src/**/*.penguin"
]
libs=["../shared/libfoo.penguin-lib"]
flags=["-enable-coroutine"]
```

把它代替源文件传给编译器即可：`build/bootstrap/pass3 MyPenguin.penguins -o myapp`。全部键见[命名空间与工程规范](../specifications/08_NamespaceAndProject.md)。
