# EmitAndWaitEvent
## Description
Void event (anonymous broadcast): one initial parks on `wait ev`, another prints then emits the pulse. The emit wakes the waiter — every emit reaches ALL parked waiters.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the event/emit/on keywords were removed in favor of the first-class Event<T> broadcast class; EmperorPenguin still ships the old receiver-based Event and has no emit method, so it fails here and should turn green once its stdlib catches up (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

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
Args: ``
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
