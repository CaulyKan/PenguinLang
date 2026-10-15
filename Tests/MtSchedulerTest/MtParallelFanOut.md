# MtParallelFanOut
## Description
Real multi-threading under EMPEROR_THREADS=4: two Pass-10-STEALABLE pure-compute coroutines are distributed at spawn time across the worker set and run in parallel within one delta; the parent joins both. The sum is order-independent, so the assertion is deterministic regardless of placement. (Verified alongside a 4x20M-iteration variant: N=1 ~207ms vs N=4 ~60ms with identical output — the speedup itself is not asserted here, only correctness.)


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun work(id: i64) -> i64 {
    let acc : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 1000) {
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
    println("two " + cast<string>(ra + rb));
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
ExpectedStdout: EQUALS `two 5995
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `two 5995
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `two 5995
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `two 5995
`
ExpectedStderr: DISCARD
