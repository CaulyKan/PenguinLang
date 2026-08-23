# OutsideDriveOutputRejected
## Description
Permission matrix: driving another module's output from outside (`d.y = 5` in a top-level initial) is a compile error — outputs have exactly one driver: the module body (or a connect line).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports grammar (input/output declarations, construct/connect) is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

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
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot drive output port`
