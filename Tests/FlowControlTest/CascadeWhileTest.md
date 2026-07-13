# CascadeWhileTest
## Description
Nested while loops, including a while with no body braces.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let i : mut u8 = 0;
        let j : mut u8 = 0;
        while (i < 2)
            while (i < 2) {
                j = 0;
                while (j < 2) {
                    print(cast<string>(i));
                    j += 1;
                }
                i += 1;
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
ExpectedStdout: EQUALS `0011`
ExpectedStderr: DISCARD
