# EmitWaitResultEvent
## Description
Event with i32 payload: looped emit (with a bare wait between rounds) consumed by three sequential `wait` reads in another initial.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the event/emit/on keywords were removed in favor of the first-class Event<T> broadcast class; EmperorPenguin still ships the old receiver-based Event and has no emit method, so it fails here and should turn green once its stdlib catches up (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    let a : i32 = wait test_event;
    print(cast<string>(a));
    let b : i32 = wait test_event;
    print(cast<string>(b));
    let c : i32 = wait test_event;
    print(cast<string>(c));
}

initial {
    for (let i : i64 in range(0, 3)) {
        test_event.emit(cast<i32>(i));
        wait;
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
ExpectedStdout: EQUALS `012`
ExpectedStderr: DISCARD
