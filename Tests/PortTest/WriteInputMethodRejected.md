# WriteInputMethodRejected
## Description
Permission matrix: driving an input through the method form this.x.write(v) is a compile error — inputs are wired exclusively through connect (the assignment form was already rejected; this closes the method-call bypass).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class M {
    input x : i64;
    initial {
        this.x.write(1);
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
ExpectedStderr: CONTAINS `Cannot write input port 'x'`
