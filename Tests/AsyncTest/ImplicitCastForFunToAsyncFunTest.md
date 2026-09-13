# ImplicitCastForFunToAsyncFunTest
## Description
Implicit cast from fun to async_fun.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): fun→async_fun 隐式转换（EP: 无该转换规则）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
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
