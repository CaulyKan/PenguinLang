# InputPassthrough
## Description
Input passthrough (connect(this.x, inner.x), the design's composition hierarchy): the class construct wires its own input into the inner module BEFORE the outer connect binds the composer's input — a _LateSource relay defers to the real source, so top.write(10) flows through Composed.x into the inner Doubler without any hand-written forwarding process.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

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

class Composed {
    input x : i64;
    output y : i64;
    construct {
        let inner : mut Doubler = new Doubler();
        connect(this.x, inner.x);
        connect(inner.y, this.y);
    }
}

construct {
    let top : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    let c : mut Composed = new Composed();
    connect(top, c.x);
}

initial {
    top.write(10);
    let out : i64 = wait c.y;
    println(cast<string>(out));
    exit(0);
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `20
`
ExpectedStderr: DISCARD
