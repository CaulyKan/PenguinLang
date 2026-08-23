# FifoCloseWaiter
## Description
close(ch) wakes every parked (and future) waiter with a runtime error; the waiter's try/catch receives the message "channel closed".

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(2, new __builtin.FifoPolicy.backpressure());
initial {
    try {
        let v : i64 = wait q;
        println("never " + cast<string>(v));
    } catch (e) {
        println("caught: " + e.message);
    }
}
initial {
    wait 2 tick;
    q.close();
    println("closed");
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
ExpectedStdout: EQUALS `closed
caught: channel closed
`
ExpectedStderr: DISCARD
