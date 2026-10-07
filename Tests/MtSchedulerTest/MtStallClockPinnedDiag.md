# MtStallClockPinnedDiag
## Description
Stall diagnostic (b) — design doc ch.12: deltas keep converging but the virtual clock never advances while someone waits on time. The classic shape is `while (!cond) wait;`: bare wait (and, since the `wait 0` unification, `wait 0;`) is owed its next delta AT the current tick, so the retry loop pins the tick and the `wait 1000` sleeper can never fire. The scheduler counts consecutive converged deltas with pending-but-never-firing timers; past the leash it reports to stderr (DISCARDed — embeds counts) and stops with exit code 61 instead of spinning silently forever.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun retry() {
    let done : mut bool = false;
    while (!done) {
        wait;
    }
}

fun sleeper() {
    wait 1000;
    println("slept");
}

initial {
    async retry();
    async sleeper();
    println("go");
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
