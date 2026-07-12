# YieldBeforeRewriteTest
## Description
Yield implementation using _DefaultRoutine with __yield_not_finished_return, before yield rewrite.

## Apply To
* BabyPenguin

## Test Code
```
    namespace ns{
        initial {
            let v: i32[] = test1();
            for (let i : i32 in v) {
                print(cast<string>(i));
            }
        }
        class _lambda {
            fun call(this: Self) -> i32 {
                __yield_not_finished_return 1;
                __yield_not_finished_return 2;
            }
        }
        fun test1() -> i32[] {
            let owner: _lambda = new _lambda();
            return cast<i32[]>(new _DefaultRoutine<i32>(owner.call, true));
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
