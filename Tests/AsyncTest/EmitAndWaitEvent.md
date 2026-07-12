# EmitAndWaitEvent
## Description
Basic event emit and wait between initial routines.

## Apply To
* BabyPenguin

## Test Code
```
    event test_event;

    initial {
        wait test_event;
        print("2");
    }

    initial {
        print("1");
        emit test_event();
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
