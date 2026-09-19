# 32. Test Framework

PenguinLang verifies every compiler against every other compiler. The infrastructure has two halves: a **cross-compiler end-to-end suite** (one markdown file per case, driven by a single-file console runner that spawns real compiler processes) and **in-process xunit unit tests** over compiler internals. A root Makefile orchestrates builds, the bootstrap, tests, and the documentation site.

## The Markdown Test Suite (`Tests/`)

~614 cases in 33 category directories (`Tests/<Category>/<Name>.md`), driven by `Tests/PenguinTestRunner.csproj` — a plain `net10.0` console exe whose entire implementation is **`Tests/Program.cs` (2838 lines, no test SDK)**. Format reference: `Tests/Readme.md`.

### Case format

```markdown
# Test Name
## Description       (optional)
## Apply To          (required: BabyPenguin / BabyPenguin CS /
                      EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS) /
                      EmperorPenguin Pass2 / EmperorPenguin Pass3 / Prebuilt)
## Test Code         (required fenced ```penguin block)
## Compile           (Args / Env / Stdin / ExpectedExitCode / ExpectedStdout / ExpectedStderr)
## Run               (optional; omit for negative compile tests)
## Build N           (multi-stage builds, Pass2/3 only; each stage has its own Test Code)
## Run LSP           (Prebuilt-only LSP sessions)
## Skip              (unconditional skip)
```

* Stream expectations: `EQUALS` (byte-exact), `ESCAPE` (C-unescape then compare), `MATCH` (mostly-literal anchored pattern with `\d`/`\s`/`\w`/`.*` holes — used for LSP `Content-Length` counts), `CONTAINS`, `DISCARD`.
* Exit expectations: an integer, `NONZERO`, or `ANY`.
* `ExpectedStdout: EQUALS \`...\`` literals may span lines until the closing backtick; `${VAR}` env expansion applies to Args/Env/Stdin and expectations (`${PENGUIN_ROOT}`, `${WORKDIR}`).
* `BabyPenguin` runs as a single compile+run process — one exit code checked against both stages; `Compile.Args` are ignored (always `-q`). Emperor backends honor `Compile.Args` (e.g. `--enable-coroutine`, stdlib files).

### Runner architecture (`Tests/Program.cs`)

* `Main` (line 26): parse options → locate repo root (`LocateRepoRoot`, 305; sets `PENGUIN_ROOT`) → discover/parse `.md` → build work items (test × compiler) → `BootstrapGuard.Check` (114; **exit 2** if `build/bootstrap/pass2|pass3` missing — the runner never bootstraps) → auto-build the BabyPenguin Release DLL if missing (**exit 3** on failure) → create `build/testruns/<timestamp>/` → load baseline → `Parallel.ForEachAsync` over test groups (171). Exit codes: 0 all pass, 1 any fail/error, 2 bootstrap guard, 3 BabyPenguin build failure.
* **Compilers** (`BuildBackends`, 349; `ICompilerBackend`, 1392): `BabyPenguinBackend` (`dotnet BabyPenguin.dll -q <src>`, interpreted, line 1455), `BabyPenguinCsBackend` (`--backend=cs`, 1480), `EmperorOnVmBackend` (Pass1: `dotnet BabyPenguin.dll -q EmperorPenguinPass1.penguins -- <args> <src>`, 1504), `EmperorNativeBackend` (Pass2/3: `build/bootstrap/passN <args> <src>`, 1538), `PrebuiltExeBackend` (copy an exe, plus sibling `.penguin-lib`s for rpath, 1579).
* **Linking** (`EmperorLink`, 1414): the Emperor backends only emit `.ll`; the runner links via the `emperor_penguin` script — `emperor_penguin link <out>.ll -o <out>` for exes (consumer-lib closure from the `<out>.libs` side file) and `emperor_penguin link-lib <base>.ll <base>.libmeta -o <out>.penguin-lib` for libraries.
* **Artifacts**: per-combo workdir `build/testruns/<ts>/<compiler>/<category>/<test>/` with `source.penguin`, `out.exe`/`combined.ll`, `compile.log`, `run.log`, `result.json`. Every spawned stage process gets `TMPDIR = workdir` so the emperor_penguin script's temp artifacts land in the combo dir.
* **Metrics**: per stage duration (Stopwatch) and **peak RSS by polling `/proc/<pid>/status` VmHWM every 40 ms** (`ReadVmHwm`, 1371).
* **MemGate** (399–551): FIFO admission control on estimated peak RSS before spawning (Heavy 5 GiB for pass2/3 meta compiles, Light 512 MiB, live floor 1.5 GiB `MemAvailable`, 15-min give-up) — prevents the OOM killer (exit 137) under oversubscribed parallelism; also caps `--parallel`.
* **Baselines**: `build/testruns/latest.json` (default comparison), `--compare-with none|latest|<path>`; `--baseline` records the run as the new baseline (plain runs never overwrite it). Diffs flag new failures/passes/skips and time/memory regressions (`--time-regression-pct`, `--mem-regression-pct`, both default 50%).
* **Reporting**: `SummaryReporter.WriteHtml` (2301) — a self-contained `summary.html` (stat cards, per-compiler pass %, filterable table, per-stage detail overlays, dark mode); `WriteJson` (2771) writes `summary.json`.
* Options (574–627): `--compilers`, `--filter <glob|substr>`, `--probe` (ignore Apply To), `--parallel <n>` (default cores−1), `--timeout-compile 600`, `--timeout-run 60`, `--env KEY=VAL` (e.g. `EMPEROR_GC_MODE=greentea`), `--baseline`, regression thresholds.

### In-process unit tests

`BabyPenguin.Tests` / `EmperorPenguin.Tests` (xunit 2.9.3, `net10.0`): direct tests over the C# compiler API (DeclarationTests 1222 lines, Project/Mutability/Complex tests, `CSharpBackendBenchTest`), and — for EmperorPenguin — the **batch harness** (`EmperorPenguin.Tests/BatchCompiler.cs`, 548 lines): each `[Fact]` carries batch attributes (`BatchTest`/`BatchBoundTest`/`BatchIRTest`/`BatchLLVMTest`/`BatchParseTest`/`BatchTokenizeTest`); a static `Lazy<BatchResults>` compiles all of a class's cases **as one program** on the in-process `BabyPenguinVM` (each case wrapped in `namespace __test_<name>` with `println` tagged `@@name@@`; the EmperorPenguin compiler's own `.penguin` sources are loaded as source dirs), runs it, and splits output back per test. Assertions are xunit `Assert.Equal` over full expected text (actuals also dumped to `/tmp/actuals/`). The legacy `BatchE2ETest` (process-spawning) is unused.

## Build Orchestration — the Root Makefile

All artifacts live under `build/` (gitignored); every stage is a **file target with file-level dependencies** (`.penguins` source sets extracted via sed, C-runtime sources, driver scripts) — unchanged inputs are never recompiled. Header block `Makefile:1-73` documents every target.

* `make bootstrap` — the self-hosting chain: pass1 (BabyPenguin `--backend=cs` over `EmperorPenguinPass1.penguins`) → `pass2` (linked with `-enable-meta`) → `pass3` (pass2 compiles the full `EmperorPenguinPass2.penguins` with `--enable-coroutine`) → **pass4/pass5** as lib+exe pairs (`build/bootstrap/pass4.d/`, `pass5.d/`), with an **md5 convergence check** (pass4 exe == pass5 exe AND lib4 == lib5, else exit 1). Artifacts are kept, so a repeat bootstrap only re-verifies md5s.
* `make release` — one platform-independent emission (`build/release/*.ll`) re-linked per platform: `release_linux` (`build/linux/emperor_penguin` + emitter + `libemperorpenguin.penguin-lib`), `release_win` (cross-link with `-target=win64`).
* `make lsp` / `make tools` — the language server and `penguin-tools` (demangle/mangle/meta/format), linked against the release library; `make tools-test` runs `EmperorPenguin/tools/selftest.sh`.
* `make test` / `make baseline_test` — the markdown suite (`TEST_ARGS` passthrough); `make unittest` — `dotnet test`; `make publish` — both platforms + vscode extension packaging + smoke tests (bundled emperor_penguin compile+link+run, bundled LSP session, wine-run Windows LSP); `make docs-site` — this site; `make gc-bench` — GC benchmarks.

### The emperor_penguin driver script (`EmperorPenguin/emperor_penguin`, 428 lines bash)

The compiler only emits IR; this script owns the LLVM toolchain work:

* `emperor_penguin [--emitter <path>] <src...> [flags] -o <out>` — full pipeline (compile → build C runtime via `make -C EmperorPenguin/std/c OUTPUT_DIR=<tmp>` → clang link). `-o *.penguin-lib` triggers lib mode; `--lib <x.penguin-lib>` both forwards to the emitter and links the consumer lib.
* `emperor_penguin link <file.ll> -o <out> [-enable-meta] [-target=win64] [--consumer-lib <so>]...`
* `emperor_penguin link-lib <file.ll> <file.libmeta> -o <out.penguin-lib>` — links the `.so`, stamps SONAME = output basename, appends metadata + `PENGUINLIB:<offset>:<size>` footer.
* Cross linux→win via llvm-mingw (default `/opt/llvm-mingw`, `MINGW_PREFIX`/`WIN_*` overridable); `-enable-meta` needs `llvm-config` (ORC/core libs) and adds `--whole-archive libpenguin_jit.a` + `-rdynamic`; consumer libs get `-rpath $ORIGIN`; 32 MB stack flag per linker flavor; the input `.ll` is copied to `combined.ll` so identical IR links to identical binaries (md5 bootstrap convergence).

## CI (`.github/workflows/dotnet.yml`)

* **build** (ubuntu-latest): setup-dotnet → install LLVM 22 (apt.llvm.org; symlinks `clang`/`llvm-ar`/`llvm-config`) → `dotnet build` → `make bootstrap` → `make lsp tools` → `make unittest` → `make test` → upload `summary.html` artifact (always).
* **deploy-pages** (on push): mdbook 0.4.52 → `make docs-site` → publish EN book at the site root, ZH book under `/zh/`, latest test report at `/test-report/`.
