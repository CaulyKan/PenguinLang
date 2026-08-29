# VariableNetLatestWins
## Description
Wire semantics on a variable net: the connect-time seed (1) is the first delivery; two back-to-back assignments before the consumer wakes collapse to the final value (3) — latest-wins, use a FIFO when every value must arrive.

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
    let e : mut Echo = new Echo();
    connect(x, e.x);
}

initial {
    let a : i64 = wait e.y;
    x = 2;
    x = 3;
    let b : i64 = wait e.y;
    println(cast<string>(a * 10 + b));
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
ExpectedStdout: EQUALS `13
`
ExpectedStderr: DISCARD
