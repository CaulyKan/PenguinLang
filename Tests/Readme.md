# PenguinLang Markdown Test Specs

Each file under `Tests/<Category>/<Name>.md` describes one cross-compiler test case.
The runner (`Tests/PenguinTestRunner`) parses every `*.md`, compiles and (when
relevant) runs the program against each compiler listed in **Apply To**, and
checks exit codes and stdout/stderr exactly as the file specifies.

- **Category** = the parent directory name (`CalculationTest`, `Smoke`, …).
- **Test name** = the `# Title` (falls back to the filename stem if absent).
- All file paths below are relative to the repository root.

A test file is plain Markdown with a fixed set of `## Section` headers. Example:

```
# AddTest
## Description
check penguinlang basic add operator behavior

## Apply To
* BabyPenguin

## Test Code
```
initial {
    let a : u8 = 2 + 2 - 3;
    let b : string = cast<string>(a);
    print(b);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
```

## Sections

| Section | Required | Contents |
|---|---|---|
| `# Title` | recommended | The test name. |
| `## Description` | optional | Free text. |
| `## Apply To` | **required** | Bullet list of compilers (`*` or `-`). At least one. |
| `## Test Code` | **required** | A fenced code block with the penguin source. |
| `## Compile` | **required** | Compile-stage expectations. |
| `## Run` | optional | Run-stage expectations. Omit for negative/compile-only tests. |

### `## Apply To`

A bullet list. The test runs once per listed compiler and must produce the same
result on each (strict single-value semantics — see *Evaluation* below).
Recognized names (matched case-insensitively, by substring):

| Write in the md | Compiler |
|---|---|
| `BabyPenguin` | C# reference compiler/VM (interprets directly) |
| `EmperorPenguin Pass1` | EmperorPenguin compiler source run on the BabyPenguin VM (slow) |
| `EmperorPenguin Pass2` | Native `tmp/pass2` (built by `./penguin -b`) |
| `EmperorPenguin Pass3` | Native `tmp/pass3` (built by `./penguin -b`) |

Set **Apply To** to only the compilers a test is verified on. Expand it later
once more compilers agree (use `--probe` to discover agreement; see *Running*).

> Pass2/Pass3 require bootstrapped native binaries. The runner never bootstraps
> automatically — if a Pass2/3 binary is missing it exits with an error telling
> you to run `./penguin -b`. BabyPenguin and Pass1 only need `dotnet`.

#### Conditional skip (`SKIP if '<compiler>' PASS`)

An entry may be suffixed with a condition so a slow compiler is only run when a
faster one can't vouch for it:

```
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
```

Semantics: the runner first runs the **guard** compiler (`EmperorPenguin Pass2`)
for that test. If the guard **passes**, this compiler (`Pass1`) is **skipped**;
if the guard fails or isn't being run, this compiler **runs normally**. This
avoids running the slow Pass1 (EmperorPenguin on the BabyPenguin VM) when the
fast native Pass2 already confirms the result.

By default every `EmperorPenguin Pass1` entry carries
`(SKIP if 'EmperorPenguin Pass2' PASS)`. A skipped combo is recorded with
`SKIP` status (not a failure); per-compiler pass rates exclude skips from the
denominator, and the run's exit code is unaffected by skips.

### `## Test Code`

A fenced code block (```` ``` ````). Common leading indentation is stripped
automatically, so you can indent the snippet for readability.

For migrated batch cases the block may contain several `namespace __cN { … }`
blocks (symbol isolation), with the combined expected output asserted as one
value.

### `## Compile` and `## Run`

Each stage is a list of `Key: value` lines. Values may be wrapped in backticks
(`` `…` ``); empty backticks mean “empty string”.

| Key | Stage | Meaning |
|---|---|---|
| `Args` | both | Extra args. Routed per backend — see *Argument routing*. |
| `Env` | both | `KEY=VAL` tokens, whitespace-separated, e.g. `` `FOO=1 BAR=2` ``. |
| `Stdin` | Run only | Text piped to the program's stdin. |
| `ExpectedExitCode` | both | `0`, any integer, `NONZERO` (any non-zero), or `ANY`. Default `0`. |
| `ExpectedStdout` | both | `DISCARD`, or `EQUALS \`literal\``. |
| `ExpectedStderr` | both | Same as `ExpectedStdout`. |

#### Match modes

- `DISCARD` — do not check the stream.
- `EQUALS \`literal\`` — **byte-exact** match against the captured stream.
- `CONTAINS \`literal\`` — **substring** match: passes if the literal appears anywhere in the captured stream. Useful for negative tests that assert a specific error code is present in stderr.

The `EQUALS` literal is a backtick-delimited string that **may span multiple
lines** (terminated by a closing backtick), so multi-line output reads naturally:

```
ExpectedStdout: EQUALS `Hello
World
`
```

> Newlines are significant. `print(x)` emits no trailing newline; `println(x)`
> emits one. Write the expected output exactly as the program prints it.

## Evaluation

A (test × compiler) combination **passes** when every checked expectation of
every applicable stage matches.

**Stages per backend:**

- **EmperorPenguin Pass1/2/3** — *Compile* runs the compiler, which on success
  produces `out.exe`. *Run* then executes `out.exe`. Pass = compile stage OK
  **and** run stage OK.
- **BabyPenguin** — interpreted: a single process both compiles and runs the
  program. There is no separate `out.exe`. The single exit code is checked
  against both `Compile.ExpectedExitCode` and `Run.ExpectedExitCode`; the
  process stdout is checked against `Run.ExpectedStdout`. `Compile.Args` is
  ignored (BabyPenguin always runs in `-q` for clean output).

**Negative tests** — a test that expects compilation to fail: either omit the
`## Run` section entirely, or set `Compile.ExpectedExitCode` to `NONZERO` (or a
specific non-zero integer). The runner then requires the compile to exit
non-zero and skips the run stage.

## Argument routing

`Compile.Args` are placed in the backend-specific argument slot:

| Backend | Compile command (cwd = repo root) |
|---|---|
| BabyPenguin | `dotnet <BabyPenguin.dll> -q <src>` *(ignores Compile.Args)* |
| Pass1 | `dotnet <BabyPenguin.dll> -q EmperorPenguin/EmperorPenguin.penguins -- <Compile.Args> <src> -o <exe>` |
| Pass2 | `tmp/pass2 <Compile.Args> <src> -o <exe>` |
| Pass3 | `tmp/pass3 <Compile.Args> <src> -o <exe>` |

`Run.Args` are passed to the produced executable (Pass1/2/3), or become the
program's args for BabyPenguin.

## Examples

A success test (full compile→run→stdout):

```
# GcCollect
## Description
Force GC, confirm a retained node survives.

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)

## Test Code
```
class Node { val: i64; fun new(mut this, v: i64) { this.val = v; } }
initial {
    let anchor = new Node(999);
    let i: mut i64 = 0;
    while (i < 1000) { let tmp = new Node(i); i = i + 1; }
    _emperor_gc_collect();
    println(cast<string>(anchor.val));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `999
`
ExpectedStderr: DISCARD
```

A negative test (compile must fail — no `## Run` section):

```
# NegativeType
## Description
Compile must fail on an unresolved type.

## Apply To
* BabyPenguin

## Test Code
```
initial {
    let a : NoSuchType = 5;
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
```

## Running

```
dotnet run --project Tests/PenguinTestRunner -- [options] [filter]
```

| Option | Default | Meaning |
|---|---|---|
| `--compilers babypenguin,pass1,pass2,pass3[,all]` | each test's Apply To | Restrict to these compilers (intersected with Apply To). |
| `--filter <glob\|substr>` | all | Select files, e.g. `CalculationTest/*` or `AddTest`. |
| `--probe` | off | Ignore Apply To and run the selected compilers on every test — use to discover cross-compiler agreement before expanding Apply To. |
| `--parallel <n>` | cores−1 | Max concurrent (test × compiler) combinations. |
| `--timeout-compile <s>` | 600 | Per-case compile timeout. |
| `--timeout-run <s>` | 60 | Per-case run timeout. |
| `--baseline latest\|none\|<path>` | `tmp/testruns/latest.json` | Baseline for the diff. |
| `--time-regression-pct <pct>` | 50 | Flag duration regressions above this %. |
| `--mem-regression-pct <pct>` | 50 | Flag peak-memory regressions above this %. |
| `--help` | | Show help. |

Fast loop (no bootstrap needed):

```
dotnet run --project Tests/PenguinTestRunner -- --compilers babypenguin
```

Full matrix (requires `./penguin -b` first):

```
./penguin -b
dotnet run --project Tests/PenguinTestRunner
```

**Exit codes:** `0` = all pass, `1` = at least one fail/error, `2` = bootstrap
guard (a required native binary is missing).

## Artifacts and report

Each run writes to `tmp/testruns/<timestamp>/` (gitignored):

```
tmp/testruns/<ts>/
  summary.html          # interactive report — open in a browser
  summary.json          # machine-readable
  <compiler>/<category>/<test>/
    source.penguin      # the extracted Test Code
    out.exe             # EmperorPenguin only
    combined.ll         # EmperorPenguin only (via per-combo TMPDIR)
    libcore_builtin.a   # EmperorPenguin only
    compile.log         # command, exit, stdout/stderr, duration, peak RSS
    run.log             # EmperorPenguin run stage
    result.json         # per-combo outcome, expected/actual, time + memory
tmp/testruns/latest.json  # stable copy — used as next run's baseline
```

**`summary.html`** is a self-contained report (single file, no external
dependencies, light/dark via the OS theme). It shows total pass/fail/error/skip
counts, a **vs Baseline** section (new failures, new passes, time/memory
regressions vs the previous run), and a filterable table (search + per-status
and per-compiler toggles). **Click any row to expand a detail view** with
expected vs actual stdout, the compile and run commands, per-stage exit code /
duration / peak RSS / stdout / stderr / failures, the full source, and links to
the raw `compile.log`, `run.log`, and `result.json`.

Per case the report records **compile and run time and peak memory separately**
(peak RSS via `/proc/<pid>/status` `VmHWM` on Linux).
