# MtNestedAsyncChain
## Description
async-inside-async: an async function spawns its OWN child async and joins it before returning; the parent's initial joins the middle layer. Three coroutines deep, each SpawnFuture completion firing a directed wake mid-delta (the child's complete wakes the middle joiner, whose completion wakes the outer joiner — a wake CHAIN across coroutine teardowns, exercising concurrent create/destroy of the co_all list under the glock-guarded link/unlink). Deterministic value flow 1 -> +2 -> printed 3.


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
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

## Run 1
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `chain 3
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `chain 3
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `chain 3
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `chain 3
`
ExpectedStderr: DISCARD
