# OnEventTest
## Description
Subscription pattern replacing the old `on` keyword: a wait loop consumes every broadcast value (the handler body is the loop body).

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
