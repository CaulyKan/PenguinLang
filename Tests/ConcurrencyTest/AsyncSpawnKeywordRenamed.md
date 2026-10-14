# AsyncSpawnKeywordRenamed
## Description
The positive half of the resolved AsyncSpawnAfterGlobalTouchFails sentinel: the identical program with the callee renamed off the `pure` keyword compiles and runs. Two initials each spawn a free function; the pure-compute one is Pass-10 STEALABLE, the global-writing one is PINNED — both deliver results. Output order is the single-worker mt scheduler's deterministic wake order (a protocol-defined value that has flipped twice with the wake protocol, each flip stable across runs: the 2026-10-06 kick-partition fix deferred mid-poll waiters and landed `pure` first; the same day's NO-CONSUME futures fix removed the partition again — a parked waiter keeps its entry and every kick wakes it in place — restoring `global` first, verified 5/5).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Cell { v: mut i64; }
let counter : mut Cell = new Cell();

fun pure_fn(a: i64, b: i64) -> i64 {
    return a * b + 1;
}

fun touches_global(a: i64) -> i64 {
    counter.v = counter.v + a;
    return counter.v;
}

initial {
    let f1 = async pure_fn(2, 3);
    let r1 : i64 = wait f1;
    println("pure " + cast<string>(r1));
}

initial {
    let f2 = async touches_global(5);
    let r2 : i64 = wait f2;
    println("global " + cast<string>(r2));
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
ExpectedStdout: EQUALS `global 5
pure 7
`
ExpectedStderr: DISCARD
