# EmitWaitResultEvent
## Description
Event with i32 payload: looped emit (with a bare wait between rounds) consumed by three sequential `wait` reads in another initial.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
