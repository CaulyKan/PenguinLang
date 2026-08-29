# MultiInputIterateSources
## Description
Custom strategy via source iteration: the MultiInput itself is iterable — a module can walk its source views and try_poll each one (non-blocking probe) instead of waiting, e.g. to drain everything currently pending.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Sink {
    inputs : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();

    initial {
        wait 1 tick;
        let total : mut i64 = 0;
        for (let src : mut __builtin.ISource<i64> in this.inputs.iter()) {
            let v : __builtin.Option<i64> = src.try_poll();
            if (v.is_some()) {
                total = total + v.some;
            }
        }
        println(cast<string>(total));
    }
}

construct {
    let q1 : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
    let q2 : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
    let s : mut Sink = new Sink();
    connect(q1, s.inputs);
    connect(q2, s.inputs);
}

initial {
    q1.write(5);
    q2.write(7);
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `12
`
ExpectedStderr: DISCARD
