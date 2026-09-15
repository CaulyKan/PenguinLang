# EmitYieldSameTick
## Description
`Event.emit` yields one delta after broadcasting; that trailing delta must be served at the CURRENT tick even while a long timer is concurrently pending (the scheduler's idle time-jump used to skip to the timer deadline first — the emit's continuation ran at the jumped time). This is the exact shape the ESL two-phase clock relies on: emit -> settle -> commit within one tick.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let ev: mut Event<void> = new Event<void>();

async fun sleeper() {
    wait 50 tick;
}

initial {
    let f: mut IFuture<void> = async sleeper();
    ev.emit(void);
    println("emitted t=" + cast<string>(_sim_now()));
    wait 2 tick;
    println("done t=" + cast<string>(_sim_now()));
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
ExpectedStdout: EQUALS `emitted t=0
done t=2
`
ExpectedStderr: DISCARD
