# QueuedEventReceiverTest
## Description
First-class event values: a free function takes the event object as a `mut Event<i32>` parameter and emits through it — no port drilling, no global required.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
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
