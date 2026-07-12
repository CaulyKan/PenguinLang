# AsyncWithArgumentsTest
## Description
Async function with arguments.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a : IFuture<i32> = async test(1);
        let b : i32 = wait a;
        print(cast<string>(b));
    } 
    fun test(a : i32) -> i32 {
        wait;
        return a+1;
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
ExpectedStdout: EQUALS `2`
ExpectedStderr: DISCARD
