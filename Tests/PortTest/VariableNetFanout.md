# VariableNetFanout
## Description
One variable net fanned out to two consumers: each connect subscribes its own wire (independent delivery cursor), so both consumers observe every net write.

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

construct {
    let x : mut i64 = 1;
    let e1 : mut Echo = new Echo();
    let e2 : mut Echo = new Echo();
    connect(x, e1.x);
    connect(x, e2.x);
}

initial {
    let a : i64 = wait e1.y;
    let b : i64 = wait e2.y;
    x = 7;
    let c : i64 = wait e1.y;
    let d : i64 = wait e2.y;
    println(cast<string>(a + b + c + d));
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
ExpectedStdout: EQUALS `16
`
ExpectedStderr: DISCARD
