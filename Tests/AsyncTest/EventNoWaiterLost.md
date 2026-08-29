# EventNoWaiterLost
## Description
Broadcast is not queueing: a value emitted while nobody is parked is lost. The first emit (before the waiter's first wait) never arrives; only the second (emitted while parked) is delivered.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let ev : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    ev.emit(cast<i32>(7));
    wait 5 tick;
    ev.emit(cast<i32>(8));
}

initial {
    wait 2 tick;
    let v : i32 = wait ev;
    println(cast<string>(v));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `8
`
ExpectedStderr: DISCARD
