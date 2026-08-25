# EmitAndWaitEvent
## Description
Void event (anonymous broadcast): one initial parks on `wait ev`, another prints then emits the pulse. The emit wakes the waiter — every emit reaches ALL parked waiters.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let test_event : mut __builtin.Event<void> = new __builtin.Event<void>();

initial {
    wait test_event;
    print("2");
}

initial {
    print("1");
    test_event.emit(void);
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
