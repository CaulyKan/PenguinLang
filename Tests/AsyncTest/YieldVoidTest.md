# YieldVoidTest
## Description
Generator yielding void values.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
       for (let i : void in test()) {} 
    } 
    fun test() -> mut IGenerator<void> {
        print("1");
        yield;
        print("2");
        yield;
        print("3");
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
ExpectedStdout: EQUALS `123`
ExpectedStderr: DISCARD
