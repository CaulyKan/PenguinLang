# TryWriteFullReturnsFalse
## Description
try_write on a full backpressuring Fifo returns false without parking (the non-blocking escape hatch), while a write into an empty slot returns true.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(1, new __builtin.FifoPolicy.backpressure());
initial { let ok1 : bool = q.try_write(1); let ok2 : bool = q.try_write(2); println(cast<string>(ok1) + "," + cast<string>(ok2)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `true,false
`
ExpectedStderr: DISCARD
