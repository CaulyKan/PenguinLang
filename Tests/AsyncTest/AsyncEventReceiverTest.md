# AsyncEventReceiverTest
## Description
Two independent wait loops on one event: every emitted value is delivered to BOTH parked waiters (broadcast — each `wait` owns its delivery slot). Within one delta the waiters wake in spawn order, so the second one ends the program after both have printed.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the event/emit/on keywords were removed in favor of the first-class Event<T> broadcast class; EmperorPenguin still ships the old receiver-based Event and has no emit method, so it fails here and should turn green once its stdlib catches up (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    while (true) {
        let b : i32 = wait test_event;
        print("A");
        print(cast<string>(b));
    }
}

initial {
    while (true) {
        let b : i32 = wait test_event;
        print("B");
        print(cast<string>(b));
        if (b == 2) exit(0);
    }
}

initial {
    for (let i : i64 in range(0, 3)) {
        test_event.emit(cast<i32>(i));
    }
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
ExpectedStdout: EQUALS `A0B0A1B1A2B2`
ExpectedStderr: DISCARD
