# TickChannelInterleave
## Description
Timer-driven producer writes one value per increasing tick interval; the consumer accumulates across delivery rounds. Verifies tick advancement and channel delivery interleave deterministically (sum 1+2+3=6).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
initial { for (let i : i64 in range(1, 4)) { wait i tick; q.write(i); } }
initial { let sum : mut i64 = 0; for (let k : i64 in range(0, 3)) { let v : i64 = wait q; sum = sum + v; } println("sum=" + cast<string>(sum)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum=6
`
ExpectedStderr: DISCARD
