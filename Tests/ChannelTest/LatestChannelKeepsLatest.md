# LatestChannelKeepsLatest
## Description
LatestChannel (wire/diagnostic semantics): two writes before the consumer wakes collapse to the final value — use Fifo when every value must arrive.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
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
