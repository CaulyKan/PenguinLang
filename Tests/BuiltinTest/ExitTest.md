# ExitTest
## Description
Exit with code 1, which should prevent further output.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        print("hello");
        exit(1);
        print("world");
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 1
ExpectedStdout: EQUALS `hello`
ExpectedStderr: DISCARD
