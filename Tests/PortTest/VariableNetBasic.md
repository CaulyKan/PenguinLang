# VariableNetBasic
## Description
The design-doc canonical example: a `mut` variable used as a connect source becomes an implicit wire net. The connect-time value counts as one delivery (seed), and every later assignment to the variable propagates through the net: x=2 wired into f1, chained f1.y -> f2.x, the final output observed with a transaction wait is 2.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `12
`
ExpectedStderr: DISCARD
