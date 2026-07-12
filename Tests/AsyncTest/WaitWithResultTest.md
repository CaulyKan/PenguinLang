# WaitWithResultTest
## Description
Wait on async function that returns a value.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a : i32= wait test();
        print(cast<string>(a));
    }
    fun test() -> i32{
        print("1");
        wait;
        print("2");
        return 3;
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
ExpectedStdout: EQUALS `123`
ExpectedStderr: DISCARD
