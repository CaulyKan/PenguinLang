# FreeMtSpawnJoin
## Description
The free-form scheduler (--enable-multithread-scheduler) runs the same spawn/join/future machinery as sim-mt: two pure-compute async workers are joined with `wait f`. No simulation time is involved, so free-mt and sim-mt agree (design doc §6.7). Verified under EMPEROR_THREADS=4: the workers are Pass-10 STEALABLE and actually distribute; the sum is order-independent, so stdout is deterministic regardless of placement.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun work(id: i64) -> i64 {
    let acc : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 200000) {
        acc = acc + (i * id) % 7;
        i = i + 1;
    }
    return acc;
}

initial {
    let a = async work(1);
    let b = async work(2);
    let ra : i64 = wait a;
    let rb : i64 = wait b;
    println("sum " + cast<string>(ra + rb));
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
ExpectedStdout: EQUALS `sum 1199991
`
ExpectedStderr: DISCARD
