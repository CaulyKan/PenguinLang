# QuiescentBlockedWaiter
## Description
A consumer blocked forever on an empty channel with no timers and no producers is quiescence, not a hang: the program ends normally with exit code 0 (v1 has no external event sources; epoll integration will distinguish "waiting for external input" from deadlock later).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(2, new __builtin.FifoPolicy.backpressure());
initial {
    let v : i64 = wait q;
    println("never " + cast<string>(v));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS ``
ExpectedStderr: DISCARD
