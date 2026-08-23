# FifoOrder
## Description
Fifo channel basic delivery: every write is a transaction, delivered to `wait` in FIFO order (no value dedup — equal values both arrive).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
initial {
    let a : i64 = wait q;
    let b : i64 = wait q;
    let c : i64 = wait q;
    println(cast<string>(a) + cast<string>(b) + cast<string>(c));
}
initial {
    wait 1 tick;
    q.write(7);
    q.write(7);
    q.write(8);
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `778
`
ExpectedStderr: DISCARD
