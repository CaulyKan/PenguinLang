# ReadOutsideInputRejected
## Description
Permission matrix: reading another module's INPUT port from outside is a compile error — inputs are only visible inside their own module (outsiders read outputs).

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
    let v : i64 = m.x;
    println(cast<string>(v));
}
```

## Compile
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot read input port 'x' of another module`
