# MtParallelFanOut
## Description
Real multi-threading under EMPEROR_THREADS=4: two Pass-10-STEALABLE pure-compute coroutines are distributed at spawn time across the worker set and run in parallel within one delta; the parent joins both. The sum is order-independent, so the assertion is deterministic regardless of placement. (Verified alongside a 4x20M-iteration variant: N=1 ~207ms vs N=4 ~60ms with identical output — the speedup itself is not asserted here, only correctness.)

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
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `two 5995
`
ExpectedStderr: DISCARD
