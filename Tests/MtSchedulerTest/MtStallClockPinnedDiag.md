# MtStallClockPinnedDiag
## Description
Stall diagnostic (b) — design doc ch.12: deltas keep converging but the virtual clock never advances while someone waits on time. The classic shape is `while (!cond) wait;`: bare wait (and, since the `wait 0` unification, `wait 0;`) is owed its next delta AT the current tick, so the retry loop pins the tick and the `wait 1000` sleeper can never fire. The scheduler counts consecutive converged deltas with pending-but-never-firing timers; past the leash it reports to stderr (DISCARDed — embeds counts) and stops with exit code 61 instead of spinning silently forever.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
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
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=1000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 61
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
