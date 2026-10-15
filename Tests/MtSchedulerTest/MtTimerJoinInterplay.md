# MtTimerJoinInterplay
## Description
A timer wait INSIDE an async function, joined by the parent: the child parks on the virtual clock (`wait 5` — the C timer table's DIRECT wake, no futures-list churn), its SpawnFuture.complete then directed-wakes the parent's join in the same delta the timer fires. Composes the three wake sources in one chain — timer fire -> spawn completion -> joiner wake — and asserts the payload flows back through the future. The boundary advances the clock only when nothing is runnable, so the join resolves exactly at tick 5.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
async fun sleeper() -> i64 {
    wait 5;
    return 5;
}

initial {
    let f = async sleeper();
    let v : i64 = wait f;
    println("slept " + cast<string>(v) + " @" + cast<string>(__builtin._sim_now()));
    exit(0);
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
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD
