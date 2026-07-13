# CascadeIfTest
## Description
Nested if-else with cascade: else contains nested if-else with dangling-else.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        if (false) {
            print("a");
        }
        else if (1 == (1-1)) {
            print("b");
        }
        else {
            if (true) if (false) print("e"); else print("f");
        }
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
ExpectedStdout: EQUALS `f`
ExpectedStderr: DISCARD
