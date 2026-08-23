# PortCompoundAssignRejected
## Description
Permission matrix: compound assignment on a port (this.x += 1) is a compile error — the read-modify-write would bypass both the input connect-only rule and the output single-driver sugar.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class M {
    input x : i64;
    initial {
        this.x += 1;
    }
}

construct {
    let m : mut M = new M();
}

initial {
    println("a");
}
```

## Compile
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Compound assignment is not supported on port 'x'`
