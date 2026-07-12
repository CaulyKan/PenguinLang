# AsyncPollTest
## Description
Poll an async future to check its state.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a : IFuture<i32> = async test();
        let poll1 : FutureState<i32> = a.poll();
        println(cast<string>(poll1));
        wait;
        let poll2 : FutureState<i32> = a.poll();
        println(cast<string>(poll2));
    } 
    fun test() -> i32 {
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
ExpectedStdout: EQUALS `not_ready
ready_finished(1)
`
ExpectedStderr: DISCARD
