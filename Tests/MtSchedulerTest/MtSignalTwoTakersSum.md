# MtSignalTwoTakersSum
## Description
First-observer competition under the DIRECTED WAKE (batch 3.1): two takers each wait a Signal ONCE, the writer commits two values in two separate deltas (Signal is single-writer — same-delta writes would overwrite the pending slot). Each commit wakes BOTH parked takers (the wake list is cleared, stale handles no-op), exactly one consumes the freshness, the loser re-parks — with single-wait takers the matching is guaranteed complete (the first consumer RETURNS, so the second value can only reach the remaining taker). The per-taker assignment is nondeterministic; the SUM is not: 10+20 across the pair, order-independent (the design's set-comparison assertion mode). Deterministic at any EMPEROR_THREADS.


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun taker(s: mut __builtin.Signal<i64>) -> i64 {
    let v : i64 = wait s;
    return v;
}

initial {
    let s : mut __builtin.Signal<i64> = new __builtin.Signal<i64>(0);
    let a = async taker(s);
    let b = async taker(s);
    wait 0;
    s.write(10);
    wait 0;
    s.write(20);
    let va : i64 = wait a;
    let vb : i64 = wait b;
    println("sum " + cast<string>(va + vb));
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
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 30
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 30
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 30
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 30
`
ExpectedStderr: DISCARD
