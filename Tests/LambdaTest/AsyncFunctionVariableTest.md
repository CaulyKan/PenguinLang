# AsyncFunctionVariableTest
## Description
Assign async function to async_fun variable and call it.

## Apply To
* BabyPenguin

## Test Code
```
    fun x() -> i32 { 
        wait; 
        return 1;
    }
    initial {
        let y : async_fun<i32> = x;
        let z : i32 = y();
        print(cast<string>(z));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
