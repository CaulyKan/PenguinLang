# AsyncSimpleTest
## Description
Basic async function call.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        async test();
        print("1");
    } 
    fun test() {
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
