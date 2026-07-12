# BitwiseOperationTest
## Description
Bitwise AND, OR, XOR with u8.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : u8 = 30 & 15 | 10 ^ 5;
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
ExpectedStdout: EQUALS `15`
ExpectedStderr: DISCARD
