# WaitChangeVariable
## Description
The `wait change(x)` idiom migrated to the event form: `wait change(<plain variable>)` is REMOVED (a bare variable has no wake source — the writer cannot know who watches it; concurrency redesign M0). The setter assigns, then emits on an Event; the waiter parks on the event and receives the new value — the same observable 5 -> 9 -> prints 9 as the edge-detection sugar had.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let ev : mut __builtin.Event<i64> = new __builtin.Event<i64>();

initial {
    wait 2;
    ev.emit(9);
}

initial {
    let v : i64 = wait ev;
    println(cast<string>(v));
    exit(0);
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
ExpectedStdout: EQUALS `9
`
ExpectedStderr: DISCARD
