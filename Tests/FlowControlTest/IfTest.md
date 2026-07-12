# IfTest
## Description
Simple if statements with true condition, false condition, and single-line body.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        if (true) {
            print("a");
        }
        if (1 == (1-1)) {
            print("b");
        }
        if (1==1) print("c");
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
ExpectedStdout: EQUALS `ac`
ExpectedStderr: DISCARD
