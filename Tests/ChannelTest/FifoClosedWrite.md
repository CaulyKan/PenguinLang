# FifoClosedWrite
## Description
Writing onto a closed channel raises a catchable runtime error.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(2, new __builtin.FifoPolicy.backpressure());
initial {
    q.close();
    try {
        q.write(1);
        println("not reached");
    } catch (e) {
        println("caught: " + e.message);
    }
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
ExpectedStdout: EQUALS `caught: channel closed
`
ExpectedStderr: DISCARD
