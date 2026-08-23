# GlobalNetSourceRejected
## Description
Only construct-block lets and class fields become implicit nets — a top-level global referenced by a connect source is rejected with a clear error (the net hub must be initialized during elaboration).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

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

let g : mut i64 = 5;

construct {
    let e : mut Echo = new Echo();
    connect(g, e.x);
}

initial {
    println("no");
}
```

## Compile
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `must be declared inside the construct block`
