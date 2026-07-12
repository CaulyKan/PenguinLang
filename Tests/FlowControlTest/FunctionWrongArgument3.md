# FunctionWrongArgument3
## Description
Compile-error: function called with wrong argument type.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let res : u8 = add(1, cast<string>(2));
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
