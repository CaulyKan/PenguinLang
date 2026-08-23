# VariableNetBasic
## Description
The design-doc canonical example: a `mut` variable used as a connect source becomes an implicit wire net. The connect-time value counts as one delivery (seed), and every later assignment to the variable propagates through the net: x=2 wired into f1, chained f1.y -> f2.x, the final output observed with a transaction wait is 2.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Foo {
    input x : i64;
    output y : i64;
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.y = v;
        }
    }
}

construct {
    let x : mut i64 = 1;
    let f1 : mut Foo = new Foo();
    let f2 : mut Foo = new Foo();
    connect(x, f1.x);
    connect(f1.y, f2.x);
}

initial {
    let seed : i64 = wait f2.y;
    x = 2;
    let out : i64 = wait f2.y;
    println(cast<string>(seed * 10 + out));
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
ExpectedStdout: EQUALS `12
`
ExpectedStderr: DISCARD
