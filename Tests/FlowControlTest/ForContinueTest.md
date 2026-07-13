# ForContinueTest
## Description
For loop with continue on even numbers, only printing odd numbers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        for (let i : i64 in range(0, 10)) {
            if (i % 2 == 0) continue;
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
ExpectedStdout: EQUALS `13579`
ExpectedStderr: DISCARD
