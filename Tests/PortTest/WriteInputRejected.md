# WriteInputRejected
## Description
Permission matrix: writing an input port (`this.x = v`) inside the module is a compile error — inputs are wired exclusively through connect.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Bad {
    input x : i64;
    output y : i64;
    initial {
        this.x = 5;
    }
}
initial { }
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot write to input port`
