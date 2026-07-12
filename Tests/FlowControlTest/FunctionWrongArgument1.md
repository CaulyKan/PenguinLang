# FunctionWrongArgument1
## Description
Compile-error: function called with too few arguments.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let res : u8 = add(1);
        print(cast<string>(res));
    }

    fun add(a : u8, b : u8) -> u8 {
        return a + b;
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
