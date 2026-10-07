# MtSameDeltaDelivery
## Description
Fifo is a DEFERRED-COMMIT channel under the mt scheduler (design doc §8.1/§8.3): write() parks the value in the channel's pending queue; the delta boundary flushes pending into the committed queue in write order and wakes the parked readers. So a write becomes readable in the NEXT delta at the SAME tick — one delta of write->read latency (the hardware one-register-stage-per-clock model). This test pins that: write + `wait ch` completes in delta 2, not delta 1. `wait ch` still POLLS before parking (no per-delta polling loop) — the wake is the boundary's commit kick. Zero-delay same-delta visibility is the immediate containers' job (`Queue`, `List`, plain fields — same domain).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let d : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
    d.write(7);
    let x : i64 = wait d;
    println("same-delta " + cast<string>(x) + " @" + cast<string>(__builtin._sim_delta()));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `same-delta 7 @2
`
ExpectedStderr: DISCARD
