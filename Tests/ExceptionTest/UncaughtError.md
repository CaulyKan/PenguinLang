# UncaughtError
## Description
Uncaught runtime error propagates to the top level (through the scheduler) and is reported as `Uncaught runtime error: <message> (code <n>)` on stderr with a non-zero exit code; stdout produced before the error is flushed, not swallowed. (Program-raised errors carry Penguin-level numeric codes and are never mislabeled with a compiler ErrorCode that the number happens to collide with.) BabyPenguin is interpreted (compile stage = full run), so the expectation lives in the Compile section.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): language-level try/catch is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
initial {
    panic("kaboom");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Uncaught runtime error`
