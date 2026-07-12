# AdditionTest2
## Description
Basic arithmetic with i8 and variable reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let temp : i8 = 1;
        let a : i8 = temp + 2 - 4 * 3 / 2;
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
ExpectedStdout: EQUALS `-3`
ExpectedStderr: DISCARD
