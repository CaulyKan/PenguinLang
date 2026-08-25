# UncaughtError
## Description
The panic reaches no try/catch: the program exits non-zero with "Uncaught runtime error" on stderr (stdout flushed first). Stage split: BabyPenguin's compile stage interprets the program (its exit IS the program exit), while EmperorPenguin's compile stage only builds the exe — the assertions therefore live in the Run section (both compilers' run stages re-execute the program).
Uncaught runtime error propagates to the top level (through the scheduler) and is reported as `Uncaught runtime error: <message> (code <n>)` on stderr with a non-zero exit code; stdout produced before the error is flushed, not swallowed. (Program-raised errors carry Penguin-level numeric codes and are never mislabeled with a compiler ErrorCode that the number happens to collide with.) BabyPenguin is interpreted (compile stage = full run), so the expectation lives in the Compile section.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    panic("kaboom");
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Uncaught runtime error`
