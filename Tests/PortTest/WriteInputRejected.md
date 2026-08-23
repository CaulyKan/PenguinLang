# WriteInputRejected
## Description
Permission matrix: writing an input port (`this.x = v`) inside the module is a compile error — inputs are wired exclusively through connect.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports grammar (input/output declarations, construct/connect) is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

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
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot write to input port`
