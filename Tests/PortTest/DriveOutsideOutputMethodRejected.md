# DriveOutsideOutputMethodRejected
## Description
Permission matrix: driving ANOTHER module's output through the method form m.y.write(v) is a compile error — outputs are driven by their owner only (closes the method-call bypass around the assignment-form check).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class M {
    output y : i64;
    initial {
        println("a");
    }
}

construct {
    let m : mut M = new M();
}

initial {
    m.y.write(1);
}
```

## Compile
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot drive output port 'y' of another module`
