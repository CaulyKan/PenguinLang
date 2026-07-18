# AssignmentTest
## Description
Compound assignment operators: +=, -=, *=, /=.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut u8 = 1;
        a += 2;
        a -= 1;
        a *= 3;
        a /= 2;
        let b : mut string = cast<mut string>(a);
        b = b;
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
ExpectedStdout: EQUALS `3`
ExpectedStderr: DISCARD
