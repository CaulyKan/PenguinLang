# EmitWaitResultEvent
## Description
Event with i32 result, looped emit with wait.

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
        for (let i : i64 in range(0, 3)) {
            emit test_event(cast<i32>(i));
            wait;
        }
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
