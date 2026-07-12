# WaitNormalFunctionTest
## Description
Wait on a normal (non-async) function call.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a : i32 = wait test();
        println(cast<string>(a));
    } 
    fun test() -> i32 {
        println("test");
        return 1;
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
ExpectedStdout: EQUALS `test
1
`
ExpectedStderr: DISCARD
