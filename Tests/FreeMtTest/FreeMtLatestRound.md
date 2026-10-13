# FreeMtLatestRound
## Description
The round-barrier sentinel for the IMMEDIATE LatestChannel under EMPEROR_THREADS=4. In the free form a push commits inline (no boundary), but transactions still accumulate one per WRITE — the two `wait 0`-paced writes are two ordered transactions drained by two single-wait takers (sum-invariant: 10+20+ids = 33/round, 990 over 30 rounds). Together with FreeMtSignalRound/FreeMtFifoRound this completes the immediate-component round-barrier family (the pop->RUNNING stale-phantom resurrection broke exactly this pacing on Event — each family member re-exposes the barrier for its component).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun waiter(c: mut __builtin.LatestChannel<i64>, id: i64) -> i64 {
    let a : i64 = wait c;
    return a + id;
}

initial {
    let total : mut i64 = 0;
    let it : mut i64 = 0;
    while (it < 30) {
        let c : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
        let w1 = async waiter(c, 1);
        let w2 = async waiter(c, 2);
        wait 0;
        c.write(10);
        wait 0;
        c.write(20);
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
Args: `--enable-free-mt`
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
