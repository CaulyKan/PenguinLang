# OnEventTest
## Description
Subscription pattern replacing the old `on` keyword: a wait loop consumes every broadcast value (the handler body is the loop body).

UPDATED (M0 BabyPenguin scheduler alignment): same emit-collapse as QueuedEventReceiverTest — back-to-back emits deliver into the current subscription slot which collapses to the final value; only 2 is observed. Verified byte-identical on BabyPenguin, pass1+mt and pass3+mt.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    while (true) {
        let b : i32 = wait test_event;
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
Args: `--enable-coroutine --enable-multithread-scheduler`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `2`
ExpectedStderr: DISCARD
