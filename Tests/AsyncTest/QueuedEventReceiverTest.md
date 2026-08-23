# QueuedEventReceiverTest
## Description
First-class event values: a free function takes the event object as a `mut Event<i32>` parameter and emits through it — no port drilling, no global required.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the event/emit/on keywords were removed in favor of the first-class Event<T> broadcast class; EmperorPenguin still ships the old receiver-based Event and has no emit method, so it fails here and should turn green once its stdlib catches up (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

fun deep(c : mut __builtin.Event<i32>) {
    for (let i : i64 in range(0, 3)) {
        c.emit(cast<i32>(i));
    }
}

initial {
    while (true) {
        let b : i32 = wait test_event;
        print(cast<string>(b));
        if (b == 2) exit(0);
    }
}

initial {
    deep(test_event);
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
