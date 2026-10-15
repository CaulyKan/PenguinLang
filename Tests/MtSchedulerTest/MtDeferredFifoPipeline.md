# MtDeferredFifoPipeline
## Description
A three-stage pipeline over deferred-commit Fifos: producer -> stage -> consumer, one channel hop each. Each hop costs exactly one delta (the boundary flush), so the value crosses two hops in two deltas and the whole exchange finishes at tick 0 — the delta structure is a function of the program, not of scheduling. Byte-identical under EMPEROR_THREADS=1 and 4 (the determinism subset's whole point: deferred components make a delta's writes order-independent by construction).


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
let a : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
let b : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());

initial {
    a.write(5);
}

initial {
    let v : i64 = wait a;
    b.write(v * 2);
}

initial {
    let r : i64 = wait b;
    println("result " + cast<string>(r) + " @" + cast<string>(__builtin._sim_delta())
        + " tick " + cast<string>(__builtin._sim_now()));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `result 10 @3 tick 0
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `result 10 @3 tick 0
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `result 10 @3 tick 0
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `result 10 @3 tick 0
`
ExpectedStderr: DISCARD
