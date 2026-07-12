# EmitWithImplicitCastWaitResultEvent
## Description
Event with implicit cast in emit, no wait between emits.

## Apply To
* BabyPenguin

## Test Code
```
    event test_event : i32;

    initial {
        let a : i32 = wait test_event;
        print(cast<string>(a));
        let b : i32 = wait test_event;
        print(cast<string>(b));
        let c : i32 = wait test_event;
        print(cast<string>(c));
    }
    
    initial {
        emit test_event(0);
        emit test_event(1);
        emit test_event(2);
    }
```

## Compile
Args: ``
Env: ``
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
