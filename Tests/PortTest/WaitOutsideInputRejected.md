# WaitOutsideInputRejected
## Description
Permission matrix: waiting on another module's INPUT port from outside is a compile error — same visibility rule as bare reads.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class M {
    input x : i64;
    initial {
        println("a");
    }
}

construct {
    let m : mut M = new M();
}

initial {
    let v : i64 = wait m.x;
}
```

## Compile
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot wait on input port 'x' of another module`
