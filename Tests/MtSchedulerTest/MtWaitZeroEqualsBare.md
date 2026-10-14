# MtWaitZeroEqualsBare
## Description
`wait 0;` and bare `wait;` are the SAME wait (design doc 0.3, M0 unification): owed the next delta at the current tick. Bug this pins: under the mt scheduler `_mt_wait_ticks` used to route n == 0 into the timer path, where `_after(0)` registers a deadline equal to now and do_wait's poll-first fast path found it "ready" — `wait 0;` returned in the SAME delta as a no-op (output `a1 a2 b1`), diverging from bare `wait;`, the classic scheduler and BabyPenguin (all `a1 b1 a2`). The n == 0 branch now parks through `_co_wait()` like a bare wait. Verified on all three references before the fix; BabyPenguin + Pass3 pin the parity, Pass2/Pass3-classic share the same expectation via TimingTest/WaitZeroSettle.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("a1");
    wait 0;
    println("a2");
}

initial {
    println("b1");
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a1
b1
a2
`
ExpectedStderr: DISCARD
