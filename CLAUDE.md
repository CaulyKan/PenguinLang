# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Penguin-lang is a concurrent-friendly programming language with C#-like syntax, inspired by C (syntax), C#/Java (garbage collection), Rust (type system), Go (coroutines), and Verilog/SystemC (concurrency).

**Main Components:**
- **BabyPenguin**: C# implementation of Penguin-lang compiler & VM, emits BabyPenguinIR (actively developed)
- **PenguinLangParser**: ANTLR4 grammar and parser for the language
- **MagellanicPenguin**: Language Server Protocol & Debug Adapter Protocol implementation
- **EmperorPenguin**: Penguin-lang compiler build from BabyPenguin, emits LLVM IR

## Compiler bootstrapping
There are different phases to bootstrapping a native full-powered emperor penguin compiler, see below:
1. EmperorPenguin pass 1: Running on BabyPenguin VM, limited features
2. EmperorPenguin pass 2: Recompile EmperorPenguin with EmperorPenguin pass 1, with limited features
3. EmperorPenguin pass 3: Recompile EmperorPenguin with EmperorPenguin pass 2, with full features 
4. EmperorPenguin pass 4: The final full featured EmperorPenguin

To run different EmperorPenguin compiler, invoke the bootstrapped binaries in `build/bootstrap/` directly (e.g. `build/bootstrap/pass3 test.penguin`; pass1 = `dotnet run --project BabyPenguin -- -q --backend=cs EmperorPenguin/EmperorPenguinPass1.penguins -- <args>`)
To build self-bootstrapping emperorpenguin, use `make bootstrap`

**The compiler only EMITS LLVM IR** (`.ll` + side files). Linking is external — `EmperorPenguin/emperor` (bash) / `emperor.bat` (Windows) check the LLVM environment, build the C runtime (`make -C EmperorPenguin/std/c`) and drive clang (native, cross linux→win, exe or `.penguin-lib`). `make release` deploys an emitter binary + driver script pair per platform; the emitted `.ll` is platform-independent (one emission, many links).

## Root Makefile

The root `Makefile` drives everything, and all build artifacts live under `build/` (gitignored). Every stage is a FILE target with file-level dependencies (`.penguins` source sets via sed, C-runtime sources, the driver scripts) — unchanged inputs are never recompiled:

```bash
make clean          # remove the build/ tree
make bootstrap      # self-bootstrap EmperorPenguin -> build/bootstrap/pass2..pass4 (+ pass5 md5 convergence; host only)
make release        # release the host platform's emitter + driver script (release_linux / release_win)
make release_linux  # -> build/linux/emperor_penguin_llvm_emitter, build/linux/libemperorpenguin.penguin-lib, build/linux/emperor_penguin
make release_win    # -> build/win/emperor_penguin_llvm_emitter.exe, build/win/emperor.bat (linux host cross-compiles via emperor)
make lsp            # LSP for the host platform (lsp_linux / lsp_win)
make lsp_linux      # -> build/linux/penguin-lsp (links the release dynlib in the same dir)
make lsp_win        # -> build/win/MagellanicPenguinLSP.exe monolith
make tools          # penguin-tools CLI for the host platform (tools_linux / tools_win)
make tools_linux    # -> build/linux/penguin-tools (demangle|mangle|meta|format; links the release dynlib)
make tools_win      # -> build/win/penguin-tools.exe monolith
make tools-test     # penguin-tools golden tests (EmperorPenguin/tools/selftest.sh)
make test           # cross-compiler markdown suite (Tests/*.md) via PenguinTestRunner; extra args via TEST_ARGS="..."
make baseline_test  # same, but records the run as the new baseline (--baseline)
make unittest       # dotnet test (BabyPenguin.Tests + EmperorPenguin.Tests; logs to build/logs/unittest.log)
make publish        # deploy release+LSP+tools into the vscode extension (emitter+script+stdlib trees), dotnet self-contained publishes, vsix
make all            # bootstrap + lsp + tools + unittest + test, in that order (default goal)
```

- **Per-platform targets** replace the old `TARGET=win` variable: `make release`/`make lsp` pick the host, `release_win`/`lsp_win` cross-compile (linux→win only; `MINGW_PREFIX`/`WIN_CC`/`WIN_CXX`/`WIN_AR`/`WIN_CLANG` overridable, defaults `/opt/llvm-mingw` — the emperor script owns the cross env). A Windows host builds natively (MSYS2 make/clang + the vendored `thirdparty/mingw-w64-x86_64-llvm-libs` package for `-enable-meta`; the bootstrap chain stays Full-monolith on win because the `.penguin-lib` pair is ELF-specific). `make publish` builds BOTH platforms on a linux host, win-only on a Windows host. `bootstrap` and `test` always target the host.
- Because the `.ll` is platform-independent, `build/release/*.ll` is emitted ONCE and only re-linked per platform (`build/linux/`, `build/win/`).
- The pass5 convergence artifacts are kept after a successful check — a repeat `make bootstrap` with unchanged inputs only re-verifies md5s.
- `WINE=<path>` supplies the wine binary for the windows publish smoke test on a linux host.

## Build and Development Commands

```bash
# Build the entire solution
dotnet build

# Run all tests
dotnet test

# Run specific test project
dotnet test BabyPenguin.Tests

# Run single test (example)
dotnet test --filter "FullyQualifiedName~BuiltinTest.PrintTest"

# Run the cross-compiler markdown test suite (Tests/*.md) via PenguinTestRunner
# Fast loop — BabyPenguin only, no bootstrap needed:
dotnet run --project Tests/PenguinTestRunner.csproj -- --compilers babypenguin
# Or through the Makefile (logs to build/test.log; extra args via TEST_ARGS):
make test TEST_ARGS="--compilers babypenguin"
# Full matrix (requires make bootstrap first, to build build/bootstrap/pass2 & build/bootstrap/pass3):
make test                                             # all compilers in each test's Apply To
make test TEST_ARGS="--filter CalculationTest/* --compilers babypenguin,pass1"

# Run a Penguin program
dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin

# Build self-contained executables + VSCode extension + native compilers (per-TARGET or both)
make publish
make publish TARGET=win

# Build VSCode extension only
cd MagellanicPenguin\vscode && npm run package
```

## AI development workflow
1. When creating a plan, write to .agents/plans
2. Use skills in .agents/skills
3. Write project memory in .agents/memory
4. When developing a new feature, create a branch with feature name, make commit at every milestones

## Important Tips
* When writing penguinlang code, use skill penguin
* Always use max effort to implement function and test cases. Never use a easy but incorrect solution.
* When writing unit tests, you must use try to compare if full test output is correct. The use of ambiguous assertions is **prohibited**, such as comparing only queue sizes, string contains, etc.
* **Any bug or unexpected behavior you hit during work that can be reproduced with a minimal PenguinLang program MUST be persisted as a markdown test case** under `Tests/<Category>/<Name>.md` (format in `Tests/Readme.md`). This is mandatory, not optional — do this the moment you have a minimal repro, and never discard a working repro.
  - **Fixed bug**: assert the now-correct behavior (exit code + stdout); set `Apply To` to the compiler(s) the fix was verified on. This locks in the fix against regressions.
  - **Unfixed / deferred bug**: still persist it, as a **red regression sentinel**. Write the minimal repro, assert the *correct* intended behavior (what a working compiler should do), set `Apply To` to include both a known-good compiler (usually `BabyPenguin`, the C# reference) and the buggy compiler(s), and say so explicitly in `## Description` (root cause, where in the source, and "should turn green once fixed"). Example: `Tests/GenericTest/GenericClassShadowedByBuiltin.md` captures the `%t0 undefined` EmperorPenguin bug — green on BabyPenguin, red on EmperorPenguin, auto-passes the day the bug is fixed. The test runner keeps a stable baseline, so a known-red sentinel shows as a steady failure (not a new regression) until it goes green.
  - Trim the repro to the smallest program that still triggers the issue before committing it.
  The legacy `[BatchE2ETest]` cases in `EmperorPenguin.Tests/EndToEnd*Test.cs` are being phased out in favor of these `Tests/*.md` cases.

## Markdown Test Framework (cross-compiler e2e)

The cross-compiler end-to-end test suite lives in `Tests/` as **one markdown file per test case** (`Tests/<Category>/<Name>.md`, currently ~194 cases). It is driven by a single-file C# console runner — `Tests/PenguinTestRunner.csproj` (`Tests/Program.cs`, added to the .sln, **no** test SDK / xunit) — which spawns the compilers as processes. Full spec and examples: `Tests/Readme.md`.

Each `*.md` describes a penguin program, the compilers it **Apply To** (`BabyPenguin`, `EmperorPenguin Pass1`, `EmperorPenguin Pass2`, `EmperorPenguin Pass3`), and the expected compile/run exit codes and stdout. The runner compiles (+ runs the produced exe, for EmperorPenguin) each program against each applicable compiler and checks results **byte-exact** (`ExpectedStdout: EQUALS \`...\``; `DISCARD` to skip a stream; `ExpectedExitCode` may be `0`, any int, `NONZERO`, or `ANY`). Omit the `## Run` section (or set a non-zero compile exit) for negative/compile-failure tests.

- **Strict single-value**: a combination passes only if *every* compiler in its Apply To matches the expected output exactly. Set Apply To to only the compilers a test is verified on and expand later — use `--probe` to discover whether another compiler now agrees.
- **Argument routing**: `Compile.Args` are appended in the backend-specific slot — EmperorPenguin (Pass1/2/3) honors them; BabyPenguin ignores them and always runs in `-q` for clean output (see note below).
- **Bootstrap is manual**: Pass2/Pass3 require native binaries `build/bootstrap/pass2`/`build/bootstrap/pass3` (built by `make bootstrap`). The runner **never** bootstraps; if a required binary is missing it exits non-zero telling you to run `make bootstrap`. The EmperorPenguin backends only EMIT `.ll`; the runner links every emission through `EmperorPenguin/emperor link/link-lib`. Pass1 and BabyPenguin only need `dotnet`.
- **Per-case metrics**: each combination records compile and run **duration and peak RSS**.
- **Artifacts & report**: each run writes `build/testruns/<timestamp>/<compiler>/<category>/<test>/` with `source.penguin`, `out.exe`/`combined.ll`/`libcore_builtin.a` (EmperorPenguin only — routed there via per-combo `TMPDIR`), `compile.log`, `run.log`, `result.json`; plus a self-contained, interactive **`summary.html`** (open in a browser). The HTML shows the git commit (with `*` if dirty) and per-compiler pass % (green at 100%, else red); the table groups by test with one row per compiler (status pill + vs-baseline badge + time/RSS + a summary column); filters (search + per-status + per-compiler toggles) drill into the compiler rows; clicking a test opens a full-page detail (source, the expectations shown once, then per-compiler compile/run stages). `build/testruns/latest.json` is diffed against to flag new failures / new passes and time & memory regressions.
- **Exit code**: `0` iff all executed (test × compiler) combinations pass; non-zero on any fail/error.

```bash
dotnet run --project Tests/PenguinTestRunner.csproj -- [options] [filter]
  --compilers babypenguin,pass1,pass2,pass3   # default: each test's Apply To
  --filter <glob|substr>     # e.g. CalculationTest/* or AddTest
  --probe                    # ignore Apply To; run selected compilers on every test
  --parallel <n>             # default cores-1
  --timeout-compile <s>      # default 600   --timeout-run <s>   default 60
  --compare-with <path>                    # baseline to diff against: latest|none|<.json path>
                                           # default build/testruns/latest.json
  --baseline                               # flag (no value): record this run as the new
                                           # baseline — writes build/testruns/baseline-<ts>.json
                                           # and copies it to latest.json. The diff compares
                                           # against --compare-with (default latest.json);
                                           # plain runs (no --baseline) never overwrite it
  --time-regression-pct <pct>  --mem-regression-pct <pct>   # both default 50
```

> **BabyPenguin `-q` note**: `dotnet <BabyPenguin.dll> -q <file>` emits program output exactly once. (Previously `Print()` wrote to both the live console and the `CollectOutput()` buffer, so `-q` doubled the output; `BabyPenguin/Program.cs` now silences the live echo in quiet mode.) This is the invocation the BabyPenguin backend relies on.

## Debugging with MCP DAP Tools

Claude Code has access to the `penguin-debug` MCP server for debugging PenguinLang programs via DAP (Debug Adapter Protocol). Use these tools to inspect program behavior step by step.

### Typical Debug Workflow

```
1. penguin_debug_launch    → Start debug session (with optional breakpoints)
2. penguin_debug_step_over → Step through code
3. penguin_debug_variables → Inspect variable values
4. penguin_debug_stack_trace → View call stack
5. penguin_debug_continue  → Continue to next breakpoint or completion
6. penguin_debug_output    → View compiler messages and debug logs
7. penguin_debug_disconnect → End session
```

### Available Tools

| Tool                            | Description                                                                |
| ------------------------------- | -------------------------------------------------------------------------- |
| `penguin_debug_launch`          | Compile and start debugging. Args: `program`, `stopOnEntry`, `breakpoints` |
| `penguin_debug_set_breakpoints` | Set breakpoints. Args: `file`, `breakpoints` (array of `{line, column?}`)  |
| `penguin_debug_continue`        | Continue execution until next stop or completion                           |
| `penguin_debug_step_over`       | Step over current line                                                     |
| `penguin_debug_step_into`       | Step into function call                                                    |
| `penguin_debug_step_out`        | Step out of current function                                               |
| `penguin_debug_stack_trace`     | Get current call stack with source locations                               |
| `penguin_debug_variables`       | Get local variables (optional `variablesReference` for nested objects)     |
| `penguin_debug_evaluate`        | Evaluate an expression                                                     |
| `penguin_debug_output`          | Get diagnostic output (compiler messages, breakpoint status, debug logs)   |
| `penguin_debug_status`          | Query current debug session state                                          |
| `penguin_debug_disconnect`      | End debug session and get final output                                     |

### Example: Debug with Breakpoints

```
penguin_debug_launch({
  program: "Examples/test.penguin",
  breakpoints: [{file: "Examples/test.penguin", lines: [{line: 2}, {line: 4}]}]
})
→ Stops at line 2, shows local variables

penguin_debug_step_over()
→ Advances one step, shows updated variables

penguin_debug_continue()
→ Runs to next breakpoint (line 4) or completion

penguin_debug_output()
→ Shows compiler diagnostics and debug logs
```

### Notes
- The `initial` block in user code runs inside `_ns_<name>.initial_0` function
- Breakpoints in builtin code may trigger before reaching user code; use `continue` to skip to user breakpoints
- The MCP server source is at `MagellanicPenguin/mcp-debug/` (TypeScript + `@modelcontextprotocol/sdk`)

## Error Handling

Use `BabyPenguinException` for errors with source location information. The `ErrorReporter` class handles diagnostic output with configurable verbosity levels (0-3).

## Type System

The language has explicit types with mutability modifiers. The type system is defined in `BabyPenguin/Type/` and supports:
- Primitive types (u8-u64, i8-i64, float, double, string, bool, char)
- Complex types (class, enum, interface, fun, arrays [])
- Generic types
- Type references (type alias)

See `docs/specifications/03_DataTypes.md` for detailed type information.

## BabyPenguin Architecture (C# Implementation)

BabyPenguin is the reference compiler & VM for PenguinLang, implemented in C#. It provides both compilation and execution capabilities.

### VM Architecture
- **Register-based VM**: Each function has a register array (not stack-based)
- **Yield/Async support**: Functions yield via `IEnumerable<RuntimeFrameResult>` for concurrent execution
- **Global object tracking**: All objects tracked in `RuntimeGlobal.AllObjects` with reference IDs
- **No JIT**: Purely interpretive execution

### IR Format
BabyPenguin emits a register-based IR with 25+ instruction types:
- **Value ops**: `CONST`, `ARG`, `ASSIGN`, `CAST`
- **Arithmetic**: `BINOP`, `UNARYOP`
- **Member access**: `RDMBR`, `WRMBR`
- **Control flow**: `BR`, `BR_COND`, `RET`, `RET_VOID`
- **Calls**: `CALL`, `CALL_VOID`
- **Objects**: `NEW`, `NEW_ENUM`
- **Enums**: `ISENUM`, `RDENUM`
- **Interfaces**: `ISINSTANCE`, `BOX`, `UNBOX`
- **Globals**: `GLOBAL_LOAD`, `GLOBAL_STORE`

### Runtime Value Types (C#)
- `BasicRuntimeValue`: Primitives (bool, i8-i64, u8-u64, f32, f64, char, string)
- `ReferenceRuntimeValue`: Objects with fields and reference tracking
- `FunctionRuntimeValue`: Function references with owner (fat pointers)
- `EnumRuntimeValue`: Enum variants with payloads

### Built-in Functions (provided by BabyPenguin VM)
- I/O: `print`, `println`, `eprint`, `eprintln`, `exit`
- String: `string_length`, `string_find`, `string_find_from`, `string_substring`, `string_char_at`, `string_char_code`, `string_to_int`
- File: `file_read_text`, `file_write_text`, `mkdir`, `file_exists`, `dir_exists`, `dir_get_entries`
- Process: `_exec_cmd`
- Misc: `lshift`, `rshift`, AtomicI64 operations

### Standard Library (PenguinLang)
- `Option<T>`: some/none enum with `is_some()`, `is_none()`, `value_or()`
- `Result<T,E>`: ok/error enum with `is_ok()`, `is_error()`, `value_or()`
- `List<T>`: Linked list with `push()`, `at()`, `set()`, `pop()`, `remove()`, `size()`
- `Queue<T>`: Linked queue with `enqueue()`, `dequeue()`, `peek()`, `size()`
- `StringBuilder`: `append()`, `to_string()`
- `IStringOps`: the std string-method surface implemented for the primitive `string` (`impl IStringOps for string`, in `BabyPenguin/Builtin.penguin` + `EmperorPenguin/std/penguin/core_builtin.penguin` — keep the two mirrored): `length/is_empty/char_at/char_code(_at)/substring/slice/find(_from/_last)/contains/starts_with/ends_with/equals_ignore_case/compare/count/to_upper/to_lower/trim(_start/_end)/replace/reverse/repeat/pad_left/pad_right/split/to_int/to_double`. `split(sep)` returns a lazy `StringSplitIterator` (for-in ready, Python-like trailing empty piece). ASCII/unit-based; direct dispatch only (no interface-typed boxing of primitives). See docs/specifications/03_DataTypes.md §IStringOps.
- `Box<T>`: Simple wrapper class
- `ICopy<T>`: Interface for value-type copy semantics (implemented for all primitives)
- `IIterator<T>`, `IIterable<T>`, `IMutIterator<T>`: Iterator interfaces. `IIterable.iter()` is the read-only path (element type T as stored, callable on immutable containers); `iter_mut()` is the mutable path. For-loop desugaring picks `iter()`/`iter_mut()` by the loop variable's mutability (`let x` → `iter`, `let x : mut T`/`let mut x` → `iter_mut`); an `in` expression that is already an iterator is used as-is.
- `Pair<K,V>`: Key-value pair class

### io Standard Library (`EmperorPenguin/std/penguin/io.penguin`, EmperorPenguin native only)
Auto-loaded by `main.penguin` next to `core_builtin.penguin` for every EmperorPenguin-compiled program. Lives in `namespace std { namespace io {...} }` — nested-namespace member access (`std.io.x()`) is supported at every depth (bind_member_access chains through member-access bases carrying namespace symbols). Its externs are declared INSIDE `std.io` and route via the **universal extern→C rule**: an extern in ANY namespace (std or user code) maps to `@<full dotted name with '.' as '_'>` (`std.io.file_open` → `std_io_file_open` in `std/c/core_builtin.c`, user `mylib.foo` → `mylib_foo`); a bare top-level extern maps to its own literal name (`extern fun abs` → `@abs`, real libc — top-level externs are exempt from the per-file `_ns_` namespace); only `__builtin`/`_utils` keep the historical `_emperor_<tail>` runtime symbols. Unreferenced externs are free. Namespaced externs must be called QUALIFIED (IR preserves call-site spelling). NOT implemented in the BabyPenguin VM — io tests are Pass2/Pass3-only.
- Console: `std.io.print/println/eprint/eprintln`, `std.io.read_line() -> Option<string>` (none at exact EOF; final unterminated line delivered once; empty line = some("")), `std.io.read_all() -> string`, `std.io.stdin_lines()` lazy iterator
- `std.io.File` (IReferenceType + IMemoryDispose — the GC closes an unreachable handle): `std.io.open(path, mode) -> mut File` (fopen modes; check `is_open()`), `write/write_line -> bool`, `read_line -> Option<string>`, `read_all -> string`, `seek(pos) -> bool` (absolute), `tell -> i64`, `flush`, `close`, `dispose_mem`
- Whole-file / fs: `std.io.read_text(path) -> Option<string>` (none only when path missing), `std.io.write_text/append_text -> bool`, `std.io.size -> i64` (-1 = missing), `std.io.exists/is_file/is_dir -> bool`, `std.io.mkdir/remove/rename -> bool`, `std.io.dir_entries(path) -> string` ('\n'-joined)
- Line iterators (for-in ready, RangeIterator shape): `std.io.lines(path)`, `std.io.split_lines(text)` (CRLF-tolerant, drops trailing empty piece), `std.io.stdin_lines()`

## EmperorPenguin Architecture

EmperorPenguin is the self-hosting compiler (written in PenguinLang, compiled/run by BabyPenguin VM). It processes `.penguin` source files through a multi-pass pipeline and emits LLVM IR as its final output.

### Project Configuration

`EmperorPenguin/EmperorPenguinPass1.penguins` (formerly `EmperorPenguin.penguins`) defines source roots:
```
sources=["src/ast/*.penguin", "src/bound/*.penguin", "src/ir/*.penguin", "src/llvm/*.penguin", "src/project/*.penguin", "main.penguin"]
```

Three more project files shape the build:

- `EmperorPenguinPass2.penguins` (formerly `EmperorPenguinFull.penguins`) — the same compiler set plus the json-backed Dynlib, json/vector/hashmap/array stdlib and `_utils` (the bootstrap's pass2 monolith, and the `make publish` deployed compiler).
- `EmperorPenguinLib.penguins` — Full **minus main.penguin**: the whole compiler as `libemperorpenguin.penguin-lib` (lib mode triggers on the `.penguin-lib` output name). Its metadata is the **emperor-libmeta v1 structured symbol table** (see `std/penguin/dynlib.penguin`): `export`-marked defs + their signature/field/impl closure as declaration entries, every global (with re-bound initializer text), all `impl X for Y` edges, and VERBATIM SOURCE for files containing template/meta constructs (`#template`/`#fun`/`#specializing` — json/vector/hashmap/array/utils) so consumers can monomorphize NEW generic instances. Consumers declare-not-define the table's defs — `--libmeta=direct` (default) injects prebuilt bound defs (skeleton registration + signature backfill + the publisher's prebuilt vtables) between semantic passes 1 and 2; `--libmeta=text` materializes bodyless declaration text instead (the debug/parity path; both modes produce byte-identical `.ll`) — then call into the `.so` for method bodies and re-specialize templates locally.
- `EmperorPenguinExe.penguins` — just `main.penguin`, linked with `--lib <dir>/libemperorpenguin.penguin-lib`. The exe carries the C runtime + optional JIT (the lib's `_emperor_*`/`__builtin.*` refs bind from it via `-rdynamic`) and initializes the lib's globals (re-defined from the embedded source, interposing the `.so`'s copies through the GOT). `link_lib` stamps the lib's basename as SONAME and `link_exe` adds `-rpath,$ORIGIN`, so an exe + `.penguin-lib` pair is relocatable.

`make bootstrap` keeps pass3 as the Full monolith (the first dyn-lib-capable compiler — pass2 comes from the ANTLR-safe stub project and cannot build libs), then builds pass4/pass5 as lib+exe pairs in `build/bootstrap/pass4.d`/`build/bootstrap/pass5.d` (`build/bootstrap/pass4` is a symlink; convergence checks BOTH the exe and lib md5s; the pass5.d artifacts are KEPT so a repeat bootstrap re-verifies without recompiling). Building the compiler lib requires a JIT-capable compiler (build it with `-enable-meta`) — the compiler sources engage the meta engine during their own compilation.

### Source Structure (~16,000 lines total)

```
EmperorPenguin/src/
  ast/           -- AST layer (Parser 1808 lines, Lexer 964 lines, AST 1210 lines, Token 170 lines, SourceLocation 19 lines)
  bound/         -- Semantic analysis layer (SemanticModel core ~705 lines + one file per pass: SemanticBuildScopes ~1010, SemanticResolveTypes ~940, SemanticMonomorphize ~2120, SemanticBindBodies ~1230, SemanticBindExpressions ~2280, SemanticBindMetaCalls ~870, SemanticMetaRewrite ~790, SemanticInterfaces ~610, SemanticClassifyValueTypes ~480, SemanticValidateControlFlow ~300, SemanticBindSymbols ~160, SemanticConstructors ~150, SemanticShared ~190; plus Bound* data files)
  ir/            -- IR generation layer (IRGenerator 1278 lines, IRInstruction 600 lines, IRBuilder 203 lines, ...)
  llvm/          -- LLVM IR emission (LLVMEmitter 2553 lines)
  project/       -- Project file parsing, glob resolution (Project.penguin 396 lines)
main.penguin     -- Entry point (180 lines)
```

### Entry Point Flow (main.penguin)

1. Parse command-line args → check for `.penguins` project file or direct `.penguin` files
2. Load `EmperorPenguin/std/penguin/core_builtin.penguin` as standard library
3. Build per-file `SourceInput` list
4. `EmperorPenguinCompiler.compile_sources(inputs)` → bound compilation result
5. Check for semantic errors → report with source locations
6. `IRGenerator.generate(result)` → IR module
7. `LLVMEmitter.lower(module, result)` → LLVM IR text
8. Allocate a per-process unique temp dir via `_utils.get_temp_folder()` (so parallel invocations never collide) and write `.ll` there
9. Run `make -C EmperorPenguin/std/c OUTPUT_DIR=<temp_dir>` to build C runtime (`libcore_builtin.a`) into that temp dir
10. Run `clang <temp_dir>/combined.ll <temp_dir>/libcore_builtin.a -o <exe>` to produce the native executable (exe path defaults to `<temp_dir>/out.exe`, overridden by `-o`)

### Bound Tree (Semantic Layer)

The bound tree sits between AST and IR. Key files in `src/bound/`:

| File                                  | Contents                                                                                                                                                                                                                                                                           |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BoundType.penguin`                   | `Mutability`, `PrimitiveType`, `TypeKind`, `BoundType` class with `display_name()`, `is_same_type()`, `is_value_type()`, `is_reference_type()`, `with_mutability()`, `with_generic_args()`                                                                                         |
| `BoundTypeRegistry.penguin`           | Primitive type pre-building, `resolve_type()` lookup, `can_implicitly_cast()` rules, `can_widen_primitive()` for numeric widening                                                                                                                                                  |
| `BoundSymbol.penguin`                 | `BoundVariableSymbol`, `BoundFunctionSymbol`, `BoundTypeSymbol`, `BoundEnumMemberSymbol`, `BoundNamespaceSymbol`, `BoundFunctionParameter`, aggregated by `BoundSymbol` enum                                                                                                       |
| `BoundScope.penguin`                  | `ScopeKind` (Global/Class/Enum/Interface/Function/Block/InitialRoutine/Impl), `BoundScope` — hierarchical lookup with `lookup_symbol()`, `lookup_type_in_scope()`, `resolve_qualified()`, namespace merging                                                                        |
| `BoundExpression.penguin`             | 12 expression classes + `BoundExpression` enum                                                                                                                                                                                                                                     |
| `BoundStatement.penguin`              | 10 statement classes + `BoundStatement` enum                                                                                                                                                                                                                                       |
| `BoundDefinition.penguin`             | `BoundVTable`, 11 definition classes (Function, Class, Enum, Interface, Namespace, InitialRoutine, TypeReference, ClassField, GlobalVariable, InterfaceImpl, InterfaceForImpl) + `BoundDefinition` enum                                                                            |
| `BoundCompilationUnit.penguin`        | `SemanticError`, `BoundCompilationUnit` with definitions, global_scope, type_registry, errors                                                                                                                                                                                      |
| `BoundTreePrinter.penguin`            | Debug printer for bound tree visualization                                                                                                                                                                                                                                         |
| `EmperorPenguinCompiler.penguin`      | Top-level compiler entry: `compile_sources()` orchestrates the full pipeline                                                                                                                                                                                                       |
| `SemanticModel.penguin`               | Shared core (~705 lines): model fields (registry/scope/errors/meta state), builtin registration, `bind()` orchestrating all passes, cross-pass `catch_up_def`, `report_*`, `already_processed`/`processed_beyond` guards, shared `resolve_type_specifier` family, `trace()` (-vvv) |
| `SemanticShared.penguin`              | Free helpers + data classes: `FunctionInstantiation`, `SpecializingBlockInfo`, file-namespace naming (`file_ns_name`), def source-file dispatch (`set_def_source_file`/`ast_def_filename`), `top_level_def_scope`, `def_lookup_scope`                                              |
| `SemanticMetaRewrite.penguin`         | `MetaRewriter` — pre-pass subsystem, single entry `run_prepass(unit)`: option store, `#define`/`#if`/`#while` splicing (def-level + stmt-level), `#fun`/`#class` collection, JIT `seed_meta_engine`                                                                                |
| `SemanticBuildScopes.penguin`         | `BuildScopesPass` — pass 1 (`run(unit, result)`): bind each AST def into per-file/global scopes, `bind_definition` dispatcher + per-def binders, dyn-lib export marking, `#specializing` text helpers                                                                              |
| `SemanticResolveTypes.penguin`        | `ResolveTypesPass` — pass 2: index-aligned AST/bound type resolution + `#template` value-param substitution                                                                                                                                                                        |
| `SemanticMonomorphize.penguin`        | `MonomorphizePass` — pass 3: iterative generic specialization fixpoint, instantiation collection (types + functions, merged from the old file-tail split), name mangling, `#specializing` injection, injected-impl resolution                                                      |
| `SemanticBindSymbols.penguin`         | `BindSymbolsPass` — pass 4: parameter symbol binding                                                                                                                                                                                                                               |
| `SemanticConstructors.penguin`        | `ConstructorsPass` — pass 5: default/explicit constructor generation                                                                                                                                                                                                               |
| `SemanticInterfaces.penguin`          | `InterfacesPass` — pass 6: vtable building & inheritance merge, `impl for` processing                                                                                                                                                                                              |
| `SemanticClassifyValueTypes.penguin`  | `ClassifyValueTypesPass` — pass 7: ICopy/IRef classification + interface-usage validation (two entries: `run` + `validate_interface_usage`)                                                                                                                                        |
| `SemanticBindBodies.penguin`          | `BindBodiesPass` — pass 8a: pass entry `run(unit, result)` (current_unit lifecycle), `bind_body_for_def`/`_specialized_def`, `bind_statement` dispatch chain, try-bind statements                                                                                                  |
| `SemanticBindExpressions.penguin`     | `BindExpressionsPass` — pass 8b: expression binding (literals/identifiers, binary/unary/logical, member access, function calls, try-bind/cast/new, control-flow expressions)                                                                                                       |
| `SemanticBindMetaCalls.penguin`       | `BindMetaCallsPass` — pass 8c: `#fun` meta-call binding & JIT splicing, sizeof/address_of/load/store intrinsics, unique-name trampolines, template instantiation routing                                                                                                           |
| `SemanticValidateControlFlow.penguin` | `ValidateControlFlowPass` — pass 9: return-path completeness, break/continue validation, return-type checks                                                                                                                                                                        |

Pass classes follow one pattern: `model: mut Option<SemanticModel>` back-reference (MetaEngine.owner_model precedent, breaks the class-field default-construction cycle), single `run()` entry, per-def processors keep their original names for `catch_up_def` replay. `SemanticModel` holds all pass instances, wired in its constructor. Any new `src/bound/*.penguin` file must be added to BOTH `EmperorPenguinPass1.penguins` and `EmperorPenguinPass2.penguins` (and `EmperorPenguinLib.penguins` when it defines bound-tree code).

### Compiler Pipeline (SemanticModel) — All 9 Passes Implemented

`SemanticModel.bind()` first runs `MetaRewriter.run_prepass(unit)` (meta flattening), then the passes (each a collaborator class with a single `run()` entry):

1. **Pass 1 — Build Scopes** (`BuildScopesPass.run`): AST → BoundDefinitions + BoundScope tree + symbol registration. Handles all definition types: functions, classes, enums, interfaces, namespaces, initial routines, implementations, type references, global variables
2. **Pass 2 — Resolve Types** (`ResolveTypesPass.run`): Walks AST and bound trees in parallel; resolves `ast.TypeSpecifier` → `BoundType` for return types, parameters, fields. Handles generics, qualified names, function types, mutability
3. **Pass 3 — Monomorphize** (`MonomorphizePass.run`): Generic instantiation for classes, enums, functions. Name mangling (`Foo__i32`, `identity__string`). Iterative approach for transitive dependencies (up to 10 iterations). Fixes up `this` parameter types for specialized methods. Newly specialized defs are replayed through passes 4-8 by the core `catch_up_def`
4. **Pass 4 — Bind Symbols** (`BindSymbolsPass.run`): Binds parameter symbols and completes symbol information for functions and fields
5. **Pass 5 — Constructors** (`ConstructorsPass.run`): Creates default constructors if none exist; processes explicit constructors marked with `is_new`
6. **Pass 6 — Interface Implementation** (`InterfacesPass.run`): Builds vtables for interface implementations. Handles both class and enum interface implementations. Processes `impl for` syntax
7. **Pass 7 — Classify Value Types** (`ClassifyValueTypesPass.run` + `.validate_interface_usage`): Determines which classes are value types (ICopy) vs reference types (IRef). Classifies based on implemented interfaces and field types
8. **Pass 8 — Bind Expressions** (`BindBodiesPass.run`, dispatching to `BindExpressionsPass` and `BindMetaCallsPass`): Binds all expression and statement types — literals, identifiers, binary/unary ops, function calls, member access, if/while, code blocks, casts, new expressions. Implements type checking, boxing/unboxing for interface casts, generic method calls
9. **Pass 9 — Validate Control Flow** (`ValidateControlFlowPass.run`): Ensures non-void functions return on all paths. Validates break/continue in loops. Type checks return values

### IR Layer (`src/ir/`)

| File                       | Contents                                                                                                                                                                                                           |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `IRModule.penguin`         | Container for functions and global variables                                                                                                                                                                       |
| `IRFunction.penguin`       | Functions with parameters, instructions, registers                                                                                                                                                                 |
| `IRInstruction.penguin`    | 23 instruction types (CONST, BINOP, UNARYOP, ASSIGN, CAST, RDMBR, WRMBR, BR, BR_COND, RET, RET_VOID, CALL, CALL_VOID, CALL_VIRT, NEW, NEW_ENUM, ISENUM, RDENUM, ISINSTANCE, BOX, UNBOX, GLOBAL_LOAD, GLOBAL_STORE) |
| `IRGenerator.penguin`      | Converts bound trees to IR. Handles vtable calls, boxing/unboxing, enum pattern matching, symbol registers                                                                                                         |
| `IRBuilder.penguin`        | Helper for building IR instructions                                                                                                                                                                                |
| `IRValue.penguin`          | IR value representation                                                                                                                                                                                            |
| `IRPrinter.penguin`        | Debug printer for IR                                                                                                                                                                                               |
| `IRSourceLocation.penguin` | Source location tracking in IR                                                                                                                                                                                     |

### LLVM Emission (`src/llvm/`)

`LLVMEmitter.penguin` (2553 lines) — multi-pass LLVM IR text emitter:
1. **Pass 1**: Collect string literals as global constants
2. **Pass 2**: Build class/enum layout tables for LLVM struct definitions
3. **Pass 3**: Emit all functions, determine needed runtime declarations
4. **Final**: Combine type definitions, globals, declarations, and functions

**Type mapping**:
- Value types (ICopy) → stack-allocated LLVM structs (no metadata header)
- Reference types (IRef) → heap-allocated with metadata pointer at offset 0
- Enums → `{ ptr metadata, i32 tag, payload? }` tagged unions
- Strings → always `ref<string>` (GC-managed)
- Primitives → i8/i16/i32/i64, u8/u16/u32/u64, float, double, bool (i8), char (i32)

### C Runtime (`EmperorPenguin/std/c/`)

| File                    | Contents                                                                                                                                                                  |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `core_builtin.c`        | All built-in function implementations: print, string operations, GC allocation, type conversions (int→string, bool→string), string concatenation, file I/O, StringBuilder |
| `gc.c`                  | Conservative mark-sweep garbage collector with stack scanning. Root registration, automatic collection thresholds. Platform-specific (x86_64, aarch64)                    |
| `penguinlang_interop.c` | Runtime support: `_emperor_vtable_lookup()` for virtual dispatch, `_emperor_isinstance()` for interface checks, `_emperor_check_class()` for class type checks            |
| `Makefile`              | Builds `libcore_builtin.a` from the above sources. Accepts `OUTPUT_DIR` variable                                                                                          |

### Standard Library (`EmperorPenguin/std/penguin/`)

| File                                                                                                         | Contents                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `core_builtin.penguin`                                                                                       | `__builtin` namespace: extern function declarations (exit, print, string ops), `Option<T>`, `Result<T,E>`, `Box<T>`, `StringBuilder`, `ICopy<T>`, `ICopy` impls for all primitives, `IHash`, `IUniqueMangleName`, `IIterator<T>`, `IIterable<T>`, `IMutIterator<T>`, `Pair<K,V>`, `Range`/`RangeIterator`                                                                                                               |
| `io.penguin`                                                                                                 | `std.io` nested-namespace stdlib (auto-loaded with core_builtin; externs in `std.io` route to `std_io_*` via the universal extern→C rule — any namespaced extern maps to `<ns>_<name>`, bare top-level externs keep literal libc symbols): console (`std.io.read_line`/`read_all`/`stdin_lines`), `std.io.File` handles, whole-file/fs helpers, `std.io.lines`/`split_lines` iterators. See *io Standard Library* above |
| `array.penguin`, `vector.penguin`, `hashmap.penguin`, `json.penguin`, `dynlib.penguin`, `metaconfig.penguin` | Pass3-only bootstrap-deferred stdlib modules — NOT auto-loaded; compiled into the compiler via `EmperorPenguinPass2.penguins` or passed per-test via `Compile.Args` (e.g. `std.Array<T,N>`)                                                                                                                                                                                                                             |
| `argparse.penguin` | Clap-style command-line parsing from FIELD ANNOTATIONS (Pass2/Pass3 only; NOT auto-loaded — pass it + `vector.penguin` via `Compile.Args`). `#arg(help, short, long, required, default_text)` / `#pos_arg(help)` written directly before a field generate `_parse_arg_<field>()`/`_parse_pos_<field>()` marker functions returning `std.ArgInfo` (a dual-unit `#class`); `std.parse_args<T>()` / `std.parse_args_after_subcommand<T>()` splice `#argparse_parse` — a deferred meta call that JIT-generates the whole parse loop from `t.fields()`+`t.methods()`. Supported field types: bool, integers, f32/f64, string, `mut std.Vector<scalar>` (repeated option / rest positionals); `--` stops options; `-h/--help` prints help + exit(0); parse errors → stderr + exit(2); `std.argv()` wraps the process args. See Tests/StdlibTest/Argparse*.md |
| `src/ast/Formatter.penguin`, `src/bound/Mangling.penguin` | Compiler-lib utilities exported from `libemperorpenguin.penguin-lib` (pass2+/Lib source sets only, NOT Pass1): `emperor.format_penguin_text(text)` — the whole-document formatter the LSP and `penguin-tools format` share; the mangle pair `emperor.demangle_name(sym)` / `emperor.mangle_name(display)` — pretty-print / rebuild generic-specialization names (`mangle(demangle(x)) == x`; non-mangled input passes through like c++filt) |

(`_utils` with `List<T>`/`Queue<T>` and the file I/O externs lives in `EmperorPenguin/src/utils.penguin` — a compiler source, part of every bootstrap, not part of user-program compilations.)

### Project Handling (`src/project/`)

`Project.penguin` (396 lines) in `project` namespace:
- `PenguinProject.load(path)`: Parse `.penguins` INI-style project files
- `resolve_sources(project_dir)`: Expand glob patterns (`*`, `**`, `?`) to actual `.penguin` file paths
- Helper functions: `string_ends_with`, `string_starts_with`, `string_trim`, `split_string`, `parse_string_array`, `path_combine`, `get_parent_dir`, `glob_match`, `expand_glob`, `collect_penguin_files`

### penguin-tools (`EmperorPenguin/tools/`, linux `build/linux/penguin-tools`)

A thin CLI over the compiler lib (`--lib build/linux/libemperorpenguin.penguin-lib` — the formatter, the mangle pair and the libmeta reader all come from the lib; the tools only add CLI glue and dogfood the argparse annotations for their own options):

- `demangle|mangle [names...]` — pretty-print / rebuild generic-specialization names (`emperor.demangle_name`/`emperor.mangle_name`); reads stdin line-by-line when no names are given (c++filt-style)
- `meta <file.penguin-lib>` — inspect emperor-libmeta metadata: default summary (lib name/version/deps/instances/symbol counts/verbatim sources), `-s/--symbols` full listing (instances demangled), `--json` raw document
- `format <file>` — whole-document formatting via `emperor.format_penguin_text`: stdout by default, `-o/--output PATH` writes a file, `-i/--in-place` rewrites in place (mutually exclusive)
- `help` — command list; unknown commands exit 2

Golden tests: `make tools-test` → `EmperorPenguin/tools/selftest.sh` (includes the mangle/demangle round-trip over every real instance of the compiler lib's own libmeta).

### Verification Commands

```bash
# Cross-compiler e2e: run the markdown test suite (see Markdown Test Framework above)
dotnet run --project Tests/PenguinTestRunner.csproj -- --compilers babypenguin | tee /tmp/test.log
# After make bootstrap: full matrix across all four compilers
dotnet run --project Tests/PenguinTestRunner.csproj -- | tee /tmp/test.log

# In-process unit tests (compiler internals; AST/Bound/IR/LLVM)
dotnet test EmperorPenguin.Tests | tee /tmp/test.log

# Run all xunit unit-test projects (both BabyPenguin.Tests and EmperorPenguin.Tests)
dotnet test --verbosity normal | tee /temp/test.log
```

When doing tests, always tee full log to a file, avoid running expensive tests multiple times

### Test Infrastructure (EmperorPenguin.Tests)

End-to-end compilation tests have **migrated to the markdown framework** (`Tests/*.md` driven by `Tests/PenguinTestRunner` — see *Markdown Test Framework* above). The legacy `[BatchE2ETest]` cases in `EmperorPenguin.Tests/EndToEnd*Test.cs` are superseded and being removed.

`EmperorPenguin.Tests` now keeps only **in-process unit tests** that exercise compiler internals via `BatchCompiler` (the test runs penguin code on the in-process `BabyPenguinVM`, *not* as a separate process):
- `BatchTest` / `InitBatch<T>()` — AST/parser tests
- `BatchBoundTest` / `InitBoundBatch<T>()` — Bound tree tests
- `BatchIRTest` / `InitIRBatch<T>()` — IR generation tests
- `BatchLLVMTest` / `InitLLVMBatch<T>()` — LLVM IR tests

Test pattern:
```csharp
private static readonly BatchResults batch = BatchCompiler.InitBoundBatch<ClassName>();
[BatchBoundTest(/* penguin code */, /* expected bound-tree output */)]
[Fact]
public void TestName() => batch.Assert();
```

### PenguinLang Mutability Patterns for Bound Objects

When writing PenguinLang code that modifies nested objects:

- **Constructors use `fun new(mut this, ...)`**: Always require `mut this`
- **`let x: mut T = value`**: Creates immutable binding to mutable value — can call `mut this` methods and assign to `mut` fields
- **`let mut x = value`**: Creates mutable binding with inferred type — cannot have type annotation
- **Enum variant access returns immutable values**: Cannot chain `.symbol.some.bound_type = ...` through enum variants. Must extract to `let sym: mut BoundFunctionSymbol = ...` first
- **Functions returning `mut T`**: Required when result is assigned to `mut` fields. Change return type from `T` to `mut T`
- **`List<T>.push()` needs `mut` list**: Declare as `let params: mut List<T>` or `let mut params = new List<T>()`

### Namespace Convention for Bound Types

All bound types live in the `bound` namespace. In test code (outside the namespace), use full paths: `bound.BoundType`, `bound.BoundScope`, etc. Builtin types (`Option`, `List`, `StringBuilder`) don't need namespace prefix.

### Known Limitations (from README)

- **Concurrency/Coroutines**: Parser supports `event`, `emit`, `on`, `wait`, `async`, `folk` but LLVM emitter doesn't generate state machines for stackless coroutines yet
- **Metaprogramming**: Implemented. `#if`/`#elif`/`#else`/`#while`/`#break`/`#continue` (hardcoded compile-time control flow); `#fun` JIT-executed via LLVM ORC (native pass2+); `#typeof`, `#create_expression`/`#create_definition`, `#define`/`#defined`/`#option`. **Reflection Phase 6 Round 1 shipped** (opaque type-tokens + host callbacks: `#field_count(t)`, `#field_name(t,i)`, `#type_name(t)`, `#is_class(t)`, …). **Phase 6 v2 in progress** (real-pointer reuse: `type = emperor.BoundType`, `t.fields()`/`t.methods()`/`t.variants()` direct; per-call-site caller-stub `#fun` ABI; `#class` meta-only data structures). See `docs/specifications/10_MetaProgramming.md` and `.claude/plans/meta_plan.md` §0.4. Meta JIT runs only in native pass2/pass3 (`make bootstrap`); `dotnet test` verifies `.penguin` compiles but not the JIT path.
- **Attributes/Indexers**: Not yet implemented at the Bound layer
