# FreeMtSpawnChurn
## Description
Coroutine create/destroy churn in the FREE form under EMPEROR_THREADS=4: 100 batches of four pure-compute asyncs in flight — all four are Pass-10 STEALABLE (no shared refs, no timers), so unlike sim-mt's ESL shape they genuinely run on different worker threads with overlapping teardowns. 400 lifecycles hammering the glock-guarded co_all link/unlink and the spawn distribution inbox; the batch joins are ordered f0..f3 and the total is placement-independent (2 * sum(0..399) = 159600). The free-form twin of MtSpawnChurnParallel — same checksum, no virtual clock anywhere.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun work(v: i64) -> i64 {
    return v * 2;
}

initial {
    let total : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 100) {
        let f0 = async work(i);
        let f1 = async work(i + 100);
        let f2 = async work(i + 200);
        let f3 = async work(i + 300);
        let r0 : i64 = wait f0;
        let r1 : i64 = wait f1;
        let r2 : i64 = wait f2;
        let r3 : i64 = wait f3;
        total = total + r0 + r1 + r2 + r3;
        i = i + 1;
    }
    println("churn " + cast<string>(total));
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
ExpectedStdout: EQUALS `churn 159600
`
ExpectedStderr: DISCARD
