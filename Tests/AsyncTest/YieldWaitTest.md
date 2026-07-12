# YieldWaitTest
## Description
Generator with wait inside.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        for (let i : i32 in test()) {
            print(cast<string>(i));
        } 
    } 
    fun test() -> mut IGenerator<i32> {
        yield 1;
        wait;
        yield 2;
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
