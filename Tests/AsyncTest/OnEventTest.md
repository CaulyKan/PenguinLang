# OnEventTest
## Description
Event handler defined with `on` syntax.

## Apply To
* BabyPenguin

## Test Code
```
    event test_event : i32;

    on test_event (b: i32) {
        print(cast<string>(b));
        if (b == 2) exit(0);
    }
    
    initial {
        for (let i : i64 in range(0, 3)) {
            emit test_event(cast<i32>(i));
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
