# MtShuffleDeltasStable
## Description
The order-shuffle detector (design doc §6.8, EMPEROR_SHUFFLE_DELTAS=<seed>): at every delta start the ready queue is permuted by a seeded xorshift — a program whose output depends on WITHIN-DELTA order produces different output across seeds, while a program built on the DEFERRED components must be byte-identical under every seed. This test pins the latter: a producer/consumer pair over a deferred Fifo, whose sum is order-independent, run under seed 42 (and verified by hand against seeds 1/3/999 during development). The order-DEPENDENT case is demonstrated by five bare println initials, whose output order varies per seed (E C D B A / E D B C A / ... for seeds 1/2/3) — that class of program is exactly what the detector exists to expose.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
let ch : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());

initial {
    let i : mut i64 = 0;
    while (i < 5) {
        ch.write(i * 10);
        i += 1;
    }
}

initial {
    let acc : mut i64 = 0;
    let n : mut i64 = 0;
    while (n < 5) {
        let v : i64 = wait ch;
        acc += v;
        n += 1;
    }
    println("sum " + cast<string>(acc));
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
Env: `EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 100
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_SHUFFLE_DELTAS=42 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 100
`
ExpectedStderr: DISCARD
