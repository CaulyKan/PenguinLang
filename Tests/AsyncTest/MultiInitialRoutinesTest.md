# MultiInitialRoutinesTest
## Description
Multiple initial routines execute in sequence.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
    initial {
        print("hello ");
    } 
    initial {
        print("world");
    } 
    initial {
        print("!");
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
ExpectedStdout: EQUALS `hello world!`
ExpectedStderr: DISCARD
