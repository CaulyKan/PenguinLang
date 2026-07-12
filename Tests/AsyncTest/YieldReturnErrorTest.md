# YieldReturnErrorTest
## Description
Compile error: generator returning a non-matching type.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let v: IGenerator<i32> = test();
        for (let i : i32 in v) {
            print(cast<string>(i));
        }
    } 
    fun test() -> IGenerator<i32> {
        yield 1;
        yield 2;
        return range(3);  
        yield 3;
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
