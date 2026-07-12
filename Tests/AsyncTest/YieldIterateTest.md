# YieldIterateTest
## Description
Generator with multiple yield statements, including yield inside a loop.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let v: mut IGenerator<i64> = test();
        for (let i : i64 in v) {
            print(cast<string>(i));
        }
    }
    fun test() -> IGenerator<i64> {
        yield 1;
        yield 2;
        for (let i : i64 in range(0, 3))
            yield i + 3;
        yield 6;
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
ExpectedStdout: EQUALS `123456`
ExpectedStderr: DISCARD
