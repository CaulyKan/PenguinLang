# WaitTest
## Description
Wait in initial block yields control to other initial routines.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        print("hello");
        wait;
        print("world");
    } 
    initial {
        print(" ");
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
ExpectedStdout: EQUALS `hello world`
ExpectedStderr: DISCARD
