# GlobalNetSourceRejected
## Description
Only construct-block lets and class fields become implicit nets — a top-level global referenced by a connect source is rejected with a clear error (the net hub must be initialized during elaboration).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `must be declared inside the construct block`
