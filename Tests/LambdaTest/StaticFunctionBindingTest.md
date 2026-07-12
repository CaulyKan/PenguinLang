# StaticFunctionBindingTest
## Description
Bind static method (no this) to function variable.

## Apply To
* BabyPenguin

## Test Code
```
    namespace ns {
        class Temp {
            fun call(b : i32) -> i32 {
                return 1+b;
            }
        }
        initial {
            let x : Temp = new Temp();
            let func : fun<i32, i32> = x.call;
            print(cast<string>(func(2)));
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
ExpectedStdout: EQUALS `3`
ExpectedStderr: DISCARD
