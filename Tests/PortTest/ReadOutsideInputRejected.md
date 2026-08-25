# ReadOutsideInputRejected
## Description
Permission matrix: reading another module's INPUT port from outside is a compile error — inputs are only visible inside their own module (outsiders read outputs).

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
    let v : i64 = m.x;
    println(cast<string>(v));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot read input port 'x' of another module`
