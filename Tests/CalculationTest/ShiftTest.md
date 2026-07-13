# ShiftTest
## Description
Left shift and right shift operations using builtins.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut i64 = lshift(1, 2);
        a = rshift(a, 1);
        a = lshift(a, 2);
        a = rshift(a, 1);
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
ExpectedStdout: EQUALS `4`
ExpectedStderr: DISCARD
