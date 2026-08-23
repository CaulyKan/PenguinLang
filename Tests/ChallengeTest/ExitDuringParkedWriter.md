# ExitDuringParkedWriter
## Description
exit() while another routine is parked on a full backpressuring Fifo: the parked writer is simply abandoned — no hang, no crash, clean exit 0 with the prints that happened before parking.

## Apply To
* BabyPenguin

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(1, new __builtin.FifoPolicy.backpressure());
initial { q.write(1); println("parked"); q.write(2); println("never"); }
initial { wait 2 tick; println("bye"); exit(0); }
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `parked
bye
`
ExpectedStderr: DISCARD
