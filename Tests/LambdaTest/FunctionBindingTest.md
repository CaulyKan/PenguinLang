# FunctionBindingTest
## Description
Bind method to function variable via instance.call.

## Apply To
* BabyPenguin

## Test Code
```
    namespace ns {
        class Temp {
            a : i32 = 1;
            fun call(this: Self, b : i32) -> i32 {
                return this.a + b;
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
