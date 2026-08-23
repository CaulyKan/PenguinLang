# LatestChannelKeepsLatest
## Description
LatestChannel (wire/diagnostic semantics): two writes before the consumer wakes collapse to the final value — use Fifo when every value must arrive.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let c : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
initial {
    let a : i64 = wait c;
    println("a=" + cast<string>(a));
}
initial {
    wait 1 tick;
    c.write(1);
    c.write(2);
    println("wrote two");
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
ExpectedStdout: EQUALS `wrote two
a=2
`
ExpectedStderr: DISCARD
