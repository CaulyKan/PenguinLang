# MultiInputRoundRobin
## Description
MultiInput fairness: when both sources have pending transactions, the round-robin scan alternates — source A's queue [1,2] and source B's [9] deliver 1, 9, 2 (a busy source cannot starve the others).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): MultiInput (dynamic fan-in) exists only in BabyPenguin's builtin so far — EmperorPenguin fails here and should turn green once the ports layer lands there (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Sink {
    inputs : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();

    initial {
        let a : i64 = wait this.inputs;
        let b : i64 = wait this.inputs;
        let c : i64 = wait this.inputs;
        println(cast<string>(a * 100 + b * 10 + c));
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
    q1.write(1);
    q1.write(2);
    q2.write(9);
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `192
`
ExpectedStderr: DISCARD
