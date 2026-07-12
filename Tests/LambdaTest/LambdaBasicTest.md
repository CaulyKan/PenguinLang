# LambdaBasicTest
## Description
Basic lambda expression assigned to fun<void> and called.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x : fun<void> = fun { print("hello"); };
        x();
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
