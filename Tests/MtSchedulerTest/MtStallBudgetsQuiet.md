# MtStallBudgetsQuiet
## Description
The stall-diagnostic leashes reset on progress, so legitimate churn under TIGHT budgets stays green: the step budget resets every delta, the pin streak resets when a timer fires at a strictly later deadline (real clock progress). This program does 30 rounds, each = 100 bare-wait deltas at the same tick (pin streak ~102/round) + 4000 `wait -1` yields inside one delta (step streak 4001/delta) + a `wait 1` that advances the clock and fires (pin reset). Totals far EXCEED both budgets (3000 pin deltas > 500; 120000 steps > 10000) — if either reset regressed (streak never reset), this test turns red with exit 60/61. BabyPenguin's aligned C# scheduler implements both leashes with the same knobs — dual-pinned.

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

## Run
Args: ``
Env: `EMPEROR_SCHED_PIN_BUDGET=500 EMPEROR_SCHED_STEP_BUDGET=10000`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 30
slept
`
ExpectedStderr: DISCARD
