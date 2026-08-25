# PortCompoundAssignRejected
## Description
Permission matrix: compound assignment on a port (this.x += 1) is a compile error — the read-modify-write would bypass both the input connect-only rule and the output single-driver sugar.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Compound assignment is not supported on port 'x'`
