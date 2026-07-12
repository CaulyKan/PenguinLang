# ImplicitWaitTest
## Description
Call an async function which uses wait; execution resumes after the function completes.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        test();
        print("3");
    } 
    fun test() {
        print("1");
        wait;
        print("2");
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
