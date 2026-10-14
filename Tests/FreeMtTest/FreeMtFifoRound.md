# FreeMtFifoRound
## Description
The round-barrier sentinel for the IMMEDIATE Fifo under EMPEROR_THREADS=4: free-mt delivers inline (deliver enqueues committed + directed-wakes the parked readers), so a `wait 0`-paced producer's writes are observable the moment they land — but only to takers that parked BEFORE the write (the barrier). Two single-wait takers per round × 30 rounds: the queue's FIFO order hands 10 then 20 to whichever taker polls first; the SUM is deterministic (10+20+ids = 33/round, 990 total). Complements MtDeferredFifoPipeline (sim-mt's deferred flush) — same program shape, the two policies' delivery timing faces.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun waiter(f: mut __builtin.Fifo<i64>, id: i64) -> i64 {
    let a : i64 = wait f;
    return a + id;
}

initial {
    let total : mut i64 = 0;
    let it : mut i64 = 0;
    while (it < 30) {
        let f : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
        let w1 = async waiter(f, 1);
        let w2 = async waiter(f, 2);
        wait 0;
        f.write(10);
        wait 0;
        f.write(20);
        let r1 : i64 = wait w1;
        let r2 : i64 = wait w2;
        total = total + r1 + r2;
        it = it + 1;
    }
    println("got " + cast<string>(total));
    exit(0);
}
```

## Compile
Args: `--enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `got 990
`
ExpectedStderr: DISCARD
