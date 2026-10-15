# MtStallBudgetsQuiet
## Description
The stall-diagnostic leashes reset on progress, so legitimate churn under TIGHT budgets stays green: the step budget resets every delta, the pin streak resets when a timer fires at a strictly later deadline (real clock progress). This program does 30 rounds, each = 100 bare-wait deltas at the same tick (pin streak ~102/round) + 4000 `wait -1` yields inside one delta (step streak 4001/delta) + a `wait 1` that advances the clock and fires (pin reset). Totals far EXCEED both budgets (3000 pin deltas > 500; 120000 steps > 10000) — if either reset regressed (streak never reset), this test turns red with exit 60/61. BabyPenguin's aligned C# scheduler implements both leashes with the same knobs — dual-pinned.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun sleeper() {
    wait 1000;
    println("slept");
}

initial {
    async sleeper();
    let k : mut i64 = 0;
    while (k < 30) {
        let i : mut i64 = 0;
        while (i < 100) {
            wait;
            i = i + 1;
        }
        let j : mut i64 = 0;
        while (j < 4000) {
            wait -1;
            j = j + 1;
        }
        wait 1;
        k = k + 1;
    }
    println("done " + cast<string>(k));
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
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD
