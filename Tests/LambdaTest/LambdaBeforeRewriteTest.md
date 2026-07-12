# LambdaBeforeRewriteTest
## Description
Basic method call before lambda rewrite.

## Apply To
* BabyPenguin

## Test Code
```
    namespace ns {
        class Temp {
            fun call(this: Self, a : i32, b : i32) -> i32 {
                return a + b;
            }
        }
        initial {
            let x : Temp = new Temp();
            print(cast<string>(x.call(1,2)));
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
