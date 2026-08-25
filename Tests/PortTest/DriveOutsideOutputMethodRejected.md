# DriveOutsideOutputMethodRejected
## Description
Permission matrix: driving ANOTHER module's output through the method form m.y.write(v) is a compile error — outputs are driven by their owner only (closes the method-call bypass around the assignment-form check).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot drive output port 'y' of another module`
