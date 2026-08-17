# NegativeUnresolvableDep
## Description
A consumer `--lib` points at a `.penguin-lib` that does not exist. The loader's metadata read fails, `LibLoadState.has_error` latches, and the driver aborts with a non-zero exit and a clear message. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("unreachable");
}
```
## Build 1
Args: `--lib ${WORKDIR}/nope.penguin-lib`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Failed to read metadata`
