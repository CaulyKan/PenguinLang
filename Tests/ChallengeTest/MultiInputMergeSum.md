# MultiInputMergeSum
## Description
MultiInput aggregating two Fifo sources via add(): all six transactions (five from busy + one 99 from slow) arrive exactly once across the merged wait — count=6, sum=0+1+2+3+4+99=109. Locks in epoll-style merged waiting.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let busy : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
let slow : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
let mi : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();
initial { mi.add(cast<mut __builtin.ISource<i64>>(busy)); mi.add(cast<mut __builtin.ISource<i64>>(slow)); }
initial { for (let i : i64 in range(0, 5)) { busy.write(i); } slow.write(99); }
initial { wait 1 tick; let sum : mut i64 = 0; let cnt : mut i64 = 0; while (cnt < 6) { let v : i64 = wait mi; sum = sum + v; cnt = cnt + 1; } println("count=" + cast<string>(cnt) + ",sum=" + cast<string>(sum)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `count=6,sum=109
`
ExpectedStderr: DISCARD
