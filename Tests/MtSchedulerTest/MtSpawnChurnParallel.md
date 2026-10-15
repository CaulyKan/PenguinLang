# MtSpawnChurnParallel
## Description
Coroutine create/destroy CHURN at width 4 under EMPEROR_THREADS=4: 100 batches of four pure-compute asyncs in flight (Pass-10 STEALABLE — they genuinely distribute across the worker set), joined per batch. 400 coroutine lifecycles with overlapping teardowns — the exact shape that exposed the lock-free co_all_head corruption (concurrent destroys leaving a munmap'd node on the chain -> boundary self-heal SIGSEGV) and the pop->RUNNING phantom resurrection of the round barrier. The batch join order is fixed (f0..f3), the sum is placement-independent: 2 * sum(0..399) = 159600.


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
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
ExpectedStdout: EQUALS `churn 159600
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `churn 159600
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `churn 159600
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `churn 159600
`
ExpectedStderr: DISCARD
