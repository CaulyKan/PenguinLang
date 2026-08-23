# MergeChannelFanIn
## Description
MergeChannel fans in two sources into one arrival-ordered stream (poll prefers the first source while it has values).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let q1 : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
let q2 : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
let m : mut __builtin.MergeChannel<i64> = new __builtin.MergeChannel<i64>(cast<mut __builtin.ISource<i64>>(q1), cast<mut __builtin.ISource<i64>>(q2));
initial {
    let a : i64 = wait m;
    println("a=" + cast<string>(a));
    let b : i64 = wait m;
    println("b=" + cast<string>(b));
    let c : i64 = wait m;
    println("c=" + cast<string>(c));
}
initial {
    wait 1 tick;
    q1.write(10);
    q2.write(20);
    q1.write(11);
    println("produced");
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
ExpectedStdout: EQUALS `produced
a=10
b=11
c=20
`
ExpectedStderr: DISCARD
