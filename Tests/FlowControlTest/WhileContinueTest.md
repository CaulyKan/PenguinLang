# WhileContinueTest
## Description
Skip iteration with continue in a while loop.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let i : mut u8 = 0;
        while (i < 3) {
            i += 1;
            if (i == 2) continue;
            print(cast<string>(i));
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
ExpectedStdout: EQUALS `13`
ExpectedStderr: DISCARD
