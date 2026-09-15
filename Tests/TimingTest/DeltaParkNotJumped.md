# DeltaParkNotJumped
## Description
A bare `wait;` (one delta round) must be served at the CURRENT tick even while long timers are pending — "one delta — without touching the tick counter" (08_TimingModel). Both schedulers violated this when a concurrent timer wait existed: EmperorPenguin's idle time-jump skipped straight to the earliest pending deadline and resumed the parked coroutine at the jumped time; BabyPenguin lost the wakeup behind the timer entirely (the print after `wait;` never ran). Fixed on both sides by gating the idle jump on round ACTIVITY (EmperorPenguin __sched_run, BabyPenguin SimScheduler): the wait's own resolution marks activity, so the round that serves the delta park cannot be jumped over. (A zero-deadline-timer lowering of bare wait was tried and reverted — it livelocked Fifo backpressure writers, whose condition parks must let time advance.) Red on both compilers before the fix.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
async fun sleeper() {
    wait 100 tick;
}

initial {
    let f: mut IFuture<void> = async sleeper();
    wait 1 tick;
    println("t1 t=" + cast<string>(_sim_now()));
    wait;
    println("park t=" + cast<string>(_sim_now()));
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
ExpectedStdout: EQUALS `t1 t=1
park t=1
`
ExpectedStderr: DISCARD
