# PortFanout
## Description
One output wired to two inputs (fan-out): every write transaction reaches both consumers — each module echoes its input to its own output, and the top level verifies both echoes arrived.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports grammar (input/output declarations, construct/connect) is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Echo {
    input x : i64;
    output y : i64;
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.y.write(v);
        }
    }
}

class Source {
    output out : i64;
    initial {
        this.out.write(7);
    }
}

construct {
    let src : mut Source = new Source();
    let e1 : mut Echo = new Echo();
    let e2 : mut Echo = new Echo();
    connect(src.out, e1.x);
    connect(src.out, e2.x);
}

initial {
    let a : i64 = wait e1.y;
    let b : i64 = wait e2.y;
    println(cast<string>(a + b));
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
ExpectedStdout: EQUALS `14
`
ExpectedStderr: DISCARD
