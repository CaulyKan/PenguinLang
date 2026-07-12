# ReturnTest
## Description
Return from initial block stops execution of that block; subsequent initial blocks still run.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        print("hello");
        return;
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
ExpectedStdout: EQUALS `hello `
ExpectedStderr: DISCARD
