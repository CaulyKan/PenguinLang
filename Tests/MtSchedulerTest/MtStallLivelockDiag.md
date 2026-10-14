# MtStallLivelockDiag
## Description
Stall diagnostic (a) — design doc ch.12: a coroutine that keeps yielding without the delta ever converging (`while (true) wait -1;`, the zero-time spin) is reported and stopped with exit code 60 instead of hanging silently forever. The leash budget is per-delta coroutine steps; the run sets it low (1000) so the diagnostic fires fast, and the message goes to stderr (DISCARDed here — it embeds live counts). With the budget disabled (0) the same program hangs by design; with the 20M default it would take tens of seconds of spinning.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun spin() {
    while (true) {
        wait -1;
    }
}

initial {
    async spin();
    println("go");
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_SCHED_STEP_BUDGET=1000`
Stdin: ``
ExpectedExitCode: 60
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
