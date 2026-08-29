# WaitOutsideInputRejected
## Description
Permission matrix: waiting on another module's INPUT port from outside is a compile error — same visibility rule as bare reads.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot wait on input port 'x' of another module`
