# MtNestedAsyncChain
## Description
async-inside-async: an async function spawns its OWN child async and joins it before returning; the parent's initial joins the middle layer. Three coroutines deep, each SpawnFuture completion firing a directed wake mid-delta (the child's complete wakes the middle joiner, whose completion wakes the outer joiner — a wake CHAIN across coroutine teardowns, exercising concurrent create/destroy of the co_all list under the glock-guarded link/unlink). Deterministic value flow 1 -> +2 -> printed 3.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
async fun leaf() -> i64 {
    return 1;
}

async fun middle() -> i64 {
    let f = async leaf();
    let v : i64 = wait f;
    return v + 2;
}

initial {
    let f = async middle();
    let v : i64 = wait f;
    println("chain " + cast<string>(v));
    exit(0);
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `chain 3
`
ExpectedStderr: DISCARD
