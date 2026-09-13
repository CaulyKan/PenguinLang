# YieldNextTest
## Description
Call next() directly on a generator.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : Option<i32> = (test()).next();
        print(cast<string>(a.some));
    } 
    fun test() -> mut IGenerator<i32> {
        yield 1;
        yield 2;
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
