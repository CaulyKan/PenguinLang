# ShadowTest2
## Description
Global variable shadowed by local in initial block, with two initial blocks.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    let a : u8 = 1;
    initial {
        let a : u8 = 2;
        print(cast<string>(a));
    }

    initial {
        print(cast<string>(a));
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
ExpectedStdout: EQUALS `21`
ExpectedStderr: DISCARD
