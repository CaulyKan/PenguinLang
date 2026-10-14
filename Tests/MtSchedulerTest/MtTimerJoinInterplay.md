# MtTimerJoinInterplay
## Description
A timer wait INSIDE an async function, joined by the parent: the child parks on the virtual clock (`wait 5` — the C timer table's DIRECT wake, no futures-list churn), its SpawnFuture.complete then directed-wakes the parent's join in the same delta the timer fires. Composes the three wake sources in one chain — timer fire -> spawn completion -> joiner wake — and asserts the payload flows back through the future. The boundary advances the clock only when nothing is runnable, so the join resolves exactly at tick 5.

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

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `slept 5 @5
`
ExpectedStderr: DISCARD
