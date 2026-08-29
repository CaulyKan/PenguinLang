# OutsideDriveOutputRejected
## Description
Permission matrix: driving another module's output from outside (`d.y = 5` in a top-level initial) is a compile error — outputs have exactly one driver: the module body (or a connect line).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Doubler {
    input x : i64;
    output y : i64;
    initial { }
}
construct {
    let d : mut Doubler = new Doubler();
}
initial {
    d.y = 5;
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot drive output port`
