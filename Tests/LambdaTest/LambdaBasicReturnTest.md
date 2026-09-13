# LambdaBasicReturnTest
## Description
Lambda expression with parameters and return value.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): 带参数/返回值的 lambda（EP 同上）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

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
