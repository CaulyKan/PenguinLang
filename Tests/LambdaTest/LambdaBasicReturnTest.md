# LambdaBasicReturnTest
## Description
Lambda expression with parameters and return value.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x : fun<i32, i32, i32> = fun (a : i32, b: i32) -> i32 { return a + b; };
        print(cast<string>(x(1, 2)));
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
ExpectedStdout: EQUALS `3`
ExpectedStderr: DISCARD
