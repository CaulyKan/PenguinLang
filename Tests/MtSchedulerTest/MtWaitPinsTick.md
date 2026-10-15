# MtWaitPinsTick
## Description
The mt scheduler's bare `wait;` = `wait 0;`: it is owed the next delta AT THE CURRENT TICK and pins the clock — A wakes at tick 0 even though a 10-tick timer is pending. Under the classic scheduler the same program wakes A at tick 10 (the idle clock jump skips past an unparked bare wait; see TimingTest/DeltaParkNotJumped for the classic behavior). This is the semantic unification of docs/zh/wip/concurrent_scheduler.md §0.2/§0.3. BabyPenguin's aligned C# scheduler (SimScheduler, M0) implements the same owed-delta pinning — dual-pinned.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("A0 @" + cast<string>(__builtin._sim_now()));
    wait;
    println("A1 @" + cast<string>(__builtin._sim_now()));
}

initial {
    wait 10;
    println("B  @" + cast<string>(__builtin._sim_now()));
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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0 @0
A1 @0
B  @10
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0 @0
A1 @0
B  @10
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0 @0
A1 @0
B  @10
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0 @0
A1 @0
B  @10
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0 @0
A1 @0
B  @10
`
ExpectedStderr: DISCARD
