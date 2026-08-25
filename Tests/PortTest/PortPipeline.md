# PortPipeline
## Description
Two Doubler modules wired output→input through a top-level construct: a transaction written into the top channel flows d1.x → d1.y (×2) → d2.x → d2.y (×2) and the top-level initial waits on the final output — 21 becomes 84. This is the design-doc canonical example shape (module loop `wait input; write output` + construct wiring + top-level read).

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
            this.y.write(v * 2);
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
