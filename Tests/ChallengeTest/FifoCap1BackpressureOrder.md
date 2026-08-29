# FifoCap1BackpressureOrder
## Description
Capacity-1 backpressuring Fifo: the producer parks between writes, the consumer (starting 2 ticks later) drains all three values in order — backpressure parking plus FIFO ordering under tick-scheduled start.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(1, new __builtin.FifoPolicy.backpressure());
initial { for (let i : i64 in range(0, 3)) { q.write(i); } }
initial { wait 2 tick; let a : i64 = wait q; let b : i64 = wait q; let c : i64 = wait q; println(cast<string>(a) + cast<string>(b) + cast<string>(c)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `012
`
ExpectedStderr: DISCARD
