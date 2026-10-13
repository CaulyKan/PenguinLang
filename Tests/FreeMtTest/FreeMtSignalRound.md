# FreeMtSignalRound
## Description
The round-barrier regression sentinel for the IMMEDIATE Signal under EMPEROR_THREADS=4 — the same shape that caught the pop->RUNNING phantom bug on Event (FreeMtEventImmediate), here for Signal.write's inline commit + directed wake. Two single-wait stealable takers per round, the emitter paces with `wait 0` (park for the REST of the round — the barrier the stale-phantom resurrection violated), 30 rounds with a fresh Signal each. First-observer competition: each inline commit wakes both parked takers, one consumes; with single-wait takers the two writes match the two takers exactly, so the round total (10+20+1+2 = 33) is deterministic — 30 rounds = 990.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun waiter(s: mut __builtin.Signal<i64>, id: i64) -> i64 {
    let a : i64 = wait s;
    return a + id;
}

initial {
    let total : mut i64 = 0;
    let it : mut i64 = 0;
    while (it < 30) {
        let s : mut __builtin.Signal<i64> = new __builtin.Signal<i64>(0);
        let w1 = async waiter(s, 1);
        let w2 = async waiter(s, 2);
        wait 0;
        s.write(10);
        wait 0;
        s.write(20);
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
