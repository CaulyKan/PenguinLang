# WriteInputMethodRejected
## Description
Permission matrix: driving an input through the method form this.x.write(v) is a compile error — inputs are wired exclusively through connect (the assignment form was already rejected; this closes the method-call bypass).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot write input port 'x'`
