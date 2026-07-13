# ParenthesizedTest
## Description
Arithmetic with parentheses for correct evaluation order.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : u8 = (4 + (4 - 2) * 3) / 5;
        let b : string = cast<string>(a);
        print(b);
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
ExpectedStdout: EQUALS `2`
ExpectedStderr: DISCARD
