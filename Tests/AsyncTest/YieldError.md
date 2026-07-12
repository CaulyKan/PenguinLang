# YieldError
## Description
Compile error: yield in non-generator function.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        test();
        print("3");
    } 
    fun test() -> i32 {
        print("1");
        yield 1;
        print("2");
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
