# Compiler Usage

This page describes the PenguinLang toolchain: what the components are, how to set up a development environment, how to build the self-hosting compiler, and how to compile and run a program with the native compiler.

## Components

| Component | Language | Role |
|---|---|---|
| **BabyPenguin** | C# | Reference compiler and interpreter (VM). Runs `.penguin` directly; also lowers to C# (`--backend=cs`). Builds EmperorPenguin. |
| **PenguinLangParser** | C#/ANTLR4 | Grammar and parser library used by BabyPenguin. |
| **EmperorPenguin** | PenguinLang | Self-hosting compiler. Written in PenguinLang, compiles itself, emits LLVM IR (`.ll`); linking to native executables is driven by the `emperor` script. |
| **MagellanicPenguin** | PenguinLang/C#/TypeScript | Language server (LSP), debug adapter (DAP), and the VSCode extension. |
| **penguin-tools** | PenguinLang | CLI utilities: `demangle` / `mangle` / `meta` / `format`. |

The bootstrap relationship is the key idea: BabyPenguin (C#) compiles EmperorPenguin's sources into a native compiler; from then on EmperorPenguin compiles itself. A program compiled by any EmperorPenguin pass produces LLVM IR text; a bash/bat driver script (`EmperorPenguin/emperor`) builds the C runtime and invokes `clang` to link an executable.

## Prerequisites

* **.NET SDK 10** — BabyPenguin, the test runner, and the unit tests all target `net10.0`.
* **LLVM/clang 22 or newer** — `clang`, `llvm-ar`, and `llvm-config` on `PATH`. EmperorPenguin emits LLVM 22 IR (the debug-record form requires clang ≥ 22); `llvm-config` is needed when linking with `-enable-meta` (the JIT runtime).
* **make + bash** — the C runtime build and the `emperor` driver script.
* Optional: **mdbook 0.4.52** for `make docs-site`, **npm** for the VSCode extension package, **wine** for the Windows publish smoke test on Linux, and an **llvm-mingw** toolchain (default `/opt/llvm-mingw`) for cross-compiling Windows binaries.

## Running a Program with BabyPenguin (interpreter)

The fastest loop uses the BabyPenguin interpreter, no native toolchain needed:

```bash
dotnet run --project BabyPenguin -- Examples/HelloWorld.penguin
# or, once built:
dotnet BabyPenguin/bin/Release/net10.0/BabyPenguin.dll -q hello.penguin
```

`-q` suppresses compiler trace output so the console shows only the program's own output. BabyPenguin compiles and runs in one step; it is also the reference implementation the test suite compares native compilers against.

## Building the Native Compiler (bootstrap)

```bash
make bootstrap
```

This produces the self-hosting chain under `build/bootstrap/`:

1. **pass1** — not kept as a binary; the C# backend of BabyPenguin compiles `EmperorPenguinPass1.penguins` (EmperorPenguin without `#` meta constructs in its stdlib) straight to LLVM IR.
2. **pass2** — that IR linked into a native compiler with `-enable-meta` (JIT-capable).
3. **pass3** — pass2 compiles `EmperorPenguinPass2.penguins` (the full compiler with metaprogramming) into the first fully-capable native compiler.
4. **pass4 / pass5** — pass3 rebuilds the compiler as a shared library (`libemperorpenguin.penguin-lib`) plus a small executable; pass5 repeats the build and the Makefile checks that **md5 hashes of pass4 and pass5 converge** — the compiler reproduces itself byte-for-byte.

Every Makefile stage is a file target with file-level dependencies, so an unchanged tree rebuilds nothing — a repeated `make bootstrap` only re-verifies the md5s. Use the resulting compilers directly:

```bash
build/bootstrap/pass2 file.penguin -o out        # emits out.ll
build/bootstrap/pass3 file.penguin -o out        # emits out.ll
```

The compiler **only emits LLVM IR** (`out.ll` plus side files). It never links.

## The emperor Driver Script

`EmperorPenguin/emperor` (bash; `emperor.bat` on Windows) checks the LLVM environment, builds the C runtime (`make -C EmperorPenguin/std/c`), and drives clang:

```bash
# full pipeline: compile + build C runtime + link, in one command
./build/linux/emperor hello.penguin -o hello
./hello

# or in two steps (the test runner does this)
build/bootstrap/pass3 hello.penguin -o hello.ll-out
./build/linux/emperor link hello.ll-out.ll -o hello

# build a shared library instead of an executable
./build/linux/emperor link-lib foo.ll foo.libmeta -o foo.penguin-lib
```

Useful flags: `-enable-meta` links the LLVM ORC JIT runtime (required for compilers built from `#fun`-using sources), `--enable-coroutine` enables async/wait language support, `-target=win64` cross-links a Windows binary (Linux host, via llvm-mingw), `--lib <dir>/x.penguin-lib` compiles against a shared library, and `--emitter <path>` overrides the compiler binary. Environment overrides: `EMPEROR_PENGUIN_ROOT`, `CLANG`, `LLVM_CONFIG`, `OPT`, `MINGW_PREFIX`.

The emitted `.ll` is platform-independent: one emission can be linked for Linux, Windows, or as a `.penguin-lib`.

## Other Makefile Targets

| Target | Product |
|---|---|
| `make release` | `build/linux/emperor_penguin_llvm_emitter` (compiler), `build/linux/libemperorpenguin.penguin-lib` (compiler as a library), `build/linux/emperor_penguin` (driver script). `make release_win` cross-builds the Windows pair. |
| `make lsp` | `build/linux/penguin-lsp` — the language server, linked against the release library. |
| `make tools` | `build/linux/penguin-tools` — `demangle` / `mangle` / `meta` / `format` CLI. `make tools-test` runs its golden tests. |
| `make test` | The cross-compiler markdown suite (`Tests/*.md`, ~600 cases). Fast loop: `dotnet run --project Tests/PenguinTestRunner -- --compilers babypenguin`. |
| `make unittest` | `dotnet test` over the xunit projects. |
| `make publish` | Deploys release + LSP + tools into the VSCode extension, publishes self-contained .NET hosts, packages the `.vsix`. |
| `make docs-site` | This documentation site (English + Chinese books). |
| `make gc-bench` | GC benchmark harness. |
| `make clean` | Removes `build/`. |

## Compiling Hello World Natively — Full Walkthrough

Assuming the repository is at `~/penguinlang`:

```bash
cd ~/penguinlang
make bootstrap                      # once; produces build/bootstrap/pass2, pass3...
make release                        # optional: install the driver as build/linux/emperor_penguin

cat > hello.penguin <<'EOF'
initial {
    println("hello world from penguin-lang!");
}
EOF

# one command, full pipeline:
./build/linux/emperor_penguin hello.penguin -o hello
./hello                             # hello world from penguin-lang!
```

Or with the bootstrapped compiler and explicit link steps:

```bash
build/bootstrap/pass3 hello.penguin -o hello.exe      # emits hello.exe.ll
EMPEROR_PENGUIN_ROOT=$PWD ./build/linux/emperor link hello.exe.ll -o hello
./hello
```

`EMPEROR_PENGUIN_ROOT` is only needed when the driver script is invoked from outside the repository tree (it locates `EmperorPenguin/std` for the C runtime); the `make release` copy resolves its own location.

## VSCode Extension and the Language Server

Install the `PenguinLang` extension (packaged by `make publish`, or open `MagellanicPenguin/vscode` and run `npm run package`). Save a `.penguin` file, press F5, choose the *PenguinLang Debug* configuration, and the extension launches the bundled language server to compile and run the program with debugging support (breakpoints, stepping, variable inspection via DAP). The server understands `.penguins` project files through a `.magellanic.config` file in the workspace.

## Project Files

Single files compile directly. Larger programs use a `.penguins` project file (INI format):

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

Pass it to any compiler instead of a source file: `build/bootstrap/pass3 MyPenguin.penguins -o myapp`. See the [Namespace & Project specification](../specifications/08_NamespaceAndProject.md) for all keys.
