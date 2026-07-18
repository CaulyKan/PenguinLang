# WhileCascadeBreakContinueTest
## Description
Nested while loops with break and continue in both inner and outer loops.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let i : mut u8 = 0;
        let j : mut u8 = 0;
        while (i < 3) {
            i += 1;
            j = 0;
            while (j < 5) {
                j += 1;
                if (j == 2) continue;
                if (j == 4) break;
                print(cast<string>(j));
            }
            if (i == 2) break;
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
ExpectedStdout: EQUALS `1313`
ExpectedStderr: DISCARD
