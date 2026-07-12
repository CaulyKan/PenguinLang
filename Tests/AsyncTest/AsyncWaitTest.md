# AsyncWaitTest
## Description
Wait on an async future.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a : IFuture<i32> = async test();
        println("before");
        wait a;
        println("after");
    } 
    fun test() -> i32 {
        wait;
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
ExpectedStdout: EQUALS `before
test
after
`
ExpectedStderr: DISCARD
