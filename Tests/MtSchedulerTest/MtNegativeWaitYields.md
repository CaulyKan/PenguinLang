# MtNegativeWaitYields
## Description
`wait -1;` under the mt scheduler is a PURE YIELD (`_co_yield`): the coroutine re-queues in the CURRENT delta — the delta counter does not move and the tick is not pinned. Two yields stay in delta 1; a bare `wait;` advances one delta (and pins the tick); `wait 3` advances the clock to tick 3. The magnitude of a negative operand is meaningless (docs/zh/wip/concurrent_scheduler.md §0.3). BabyPenguin's aligned C# scheduler (SimScheduler, `_wait_ticks`'s n < 0 branch parks through the pure-yield note) implements the same semantics — dual-pinned.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let d0 : i64 = __builtin._sim_delta();
    wait -1;
    wait -1;
    let d1 : i64 = __builtin._sim_delta();
    wait;
    let d2 : i64 = __builtin._sim_delta();
    wait 3;
    let d3 : i64 = __builtin._sim_delta();
    println("deltas " + cast<string>(d0) + "," + cast<string>(d1) + "," + cast<string>(d2) + "," + cast<string>(d3)
        + " tick " + cast<string>(__builtin._sim_now()));
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
ExpectedStdout: EQUALS `deltas 1,1,2,4 tick 3
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `deltas 1,1,2,4 tick 3
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `deltas 1,1,2,4 tick 3
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `deltas 1,1,2,4 tick 3
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `deltas 1,1,2,4 tick 3
`
ExpectedStderr: DISCARD
