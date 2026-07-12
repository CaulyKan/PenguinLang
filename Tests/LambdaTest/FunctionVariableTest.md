# FunctionVariableTest
## Description
Assign function to fun<void> variable and call it.

## Apply To
* BabyPenguin

## Test Code
```
    fun x() { print("hello"); }
    initial {
        let y : fun<void> = x;
        y();
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
ExpectedStdout: EQUALS `hello`
ExpectedStderr: DISCARD
