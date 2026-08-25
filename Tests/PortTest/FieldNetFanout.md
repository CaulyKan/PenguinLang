# FieldNetFanout
## Description
A class FIELD as a connect source (connect(this.baud, ...)): the hidden net hub is a hidden instance field, the initializer value (9600) seeds it, fan-out reaches both inner consumers, and an outside assignment (b.baud = 7) propagates per instance.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Consumer {
    input clk : i64;
    output y : i64;
    initial {
        while (true) {
            let v : i64 = wait this.clk;
            this.y.write(v);
        }
    }
}

class Board {
    baud : mut i64 = 9600;
    output out : i64;
    construct {
        let c1 : mut Consumer = new Consumer();
        let c2 : mut Consumer = new Consumer();
        connect(this.baud, c1.clk);
        connect(this.baud, c2.clk);
        connect(c2.y, this.out);
    }
}

construct {
    let b : mut Board = new Board();
}

initial {
    let s0 : i64 = wait b.out;
    println(cast<string>(s0));
    b.baud = 7;
    let s1 : i64 = wait b.out;
    println(cast<string>(s1));
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
ExpectedStdout: EQUALS `9600
7
`
ExpectedStderr: DISCARD
