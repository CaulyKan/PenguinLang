# ImplicitCastForFunToAsyncFunTest
## Description
Implicit cast from fun to async_fun.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let t : async_fun<i32> = test;
        println("before");
        wait t();
        println("after");
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
ExpectedStdout: EQUALS `before
test
after
`
ExpectedStderr: DISCARD
