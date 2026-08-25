# ComposedModule
## Description
Module hierarchy: a composed class wires an inner submodule in its class-level construct — an explicit channel field feeds the inner input, and the inner output is PASSTHROUGH-wired to the composer's own output (connect(inner.y, this.y): the composer's output hub becomes the inner hub, one driver, transparent forwarding). A forwarding process in the composer bridges its own input into the inner channel.

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
            this.y = v * 2;
        }
    }
}

class Composed {
    input x : i64;
    output y : i64;
    xin : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    construct {
        let inner : mut Doubler = new Doubler();
        connect(this.xin, inner.x);
        connect(inner.y, this.y);
    }
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.xin.write(v);
        }
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
ExpectedStdout: EQUALS `20
`
ExpectedStderr: DISCARD
