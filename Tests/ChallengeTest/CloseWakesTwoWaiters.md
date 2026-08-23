# CloseWakesTwoWaiters
## Description
close() is supervised shutdown: BOTH parked waiters on the same Fifo wake with the channel-closed runtime error, both catch it and continue; the closer then exits normally. Locks in the full-cascade close semantics (design Q11/close decision).

## Apply To
* BabyPenguin

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
initial { try { let v : i64 = wait q; println("a-got"); } catch (e : __builtin.RuntimeError) { println("a-closed"); } }
initial { try { let v : i64 = wait q; println("b-got"); } catch (e : __builtin.RuntimeError) { println("b-closed"); } }
initial { wait 1 tick; q.close(); wait 1 tick; println("closed"); exit(0); }
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a-closed
b-closed
closed
`
ExpectedStderr: DISCARD
