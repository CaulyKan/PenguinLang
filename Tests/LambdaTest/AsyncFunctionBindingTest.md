# AsyncFunctionBindingTest
## Description
Bind async method to async_fun variable.

## Apply To
* BabyPenguin

## Test Code
```
    namespace ns {
        class Temp {
            a : i32 = 1;
            fun call(this: Self) -> i32 {
                wait;
                return this.a;
            }
        }
        initial {
            let x : Temp = new Temp();
            let func : async_fun<i32> = x.call;
            print(cast<string>(func()));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
