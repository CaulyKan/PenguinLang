# OutputAssignSugar
## Description
`this.y = v` on an output port desugars to `this.y.write(v)` — the RTL idiom from the design doc (module body writes its output by plain assignment; the write is a potential suspension point through the fan-out hub).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Doubler {
    input x : i64;
    output y : i64;
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.y = v * 2;
        }
    }
}

construct {
    let top : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    let d1 : mut Doubler = new Doubler();
    let d2 : mut Doubler = new Doubler();
    connect(top, d1.x);
    connect(d1.y, d2.x);
}

initial {
    top.write(21);
    let out : i64 = wait d2.y;
    println(cast<string>(out));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `84
`
ExpectedStderr: DISCARD
