# MtFifoImmediateWake
## Description
BabyPenguin's channel family stays IMMEDIATE (M0 scope; deferral is the native mt scheduler's M6 layer), so a parked `wait ch` wakes through the COARSE completion kick: the writer's write lands in the queue at once (a deferred boundary kick is only registered), and when the writer's coroutine FINISHES, the completion kick wakes every condition parker — the reader consumes in the writer's own delta (`got 9 d1`). Under the native mt scheduler the same program reads at d2 (Fifo is a deferred-commit component there; pinned by MtSameDeltaDelivery). Both are correct per their component semantics (design doc 8.3: a condition satisfied in the current delta resumes in the current delta); this test pins the C# side so the reference implementation's immediate-Fifo + coarse-kick interaction does not drift. BabyPenguin-only by design.

## Apply To
* BabyPenguin

## Test Code
```
let ch : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
initial {
    let v : i64 = wait ch;
    println("got " + cast<string>(v) + " d" + cast<string>(__builtin._sim_delta()));
}
initial {
    ch.write(9);
    println("wrote d" + cast<string>(__builtin._sim_delta()));
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `wrote d1
got 9 d1
`
ExpectedStderr: DISCARD
