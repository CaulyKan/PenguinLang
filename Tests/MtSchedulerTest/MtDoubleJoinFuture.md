# MtDoubleJoinFuture
## Description
A spawn future is NON-CONSUMING: `wait f` twice on the same completed future delivers the value both times (poll checks the done flag; the second wait is the fast path — no park, no filing). With the directed wake the first joiner is woken by SpawnFuture.complete directly (same delta, MtFutureCompletionSameDelta); the re-wait never touches the wake list. Pins the future's identity semantics — one async computation, reusable result — and that the fast-path poll (batch 3.1 removed its old global exit kick) still delivers without any kick at all.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
async fun val7() -> i64 {
    return 7;
}

initial {
    let f = async val7();
    wait 0;
    let a : i64 = wait f;
    let b : i64 = wait f;
    println("twice " + cast<string>(a + b));
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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `twice 14
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `twice 14
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `twice 14
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `twice 14
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `twice 14
`
ExpectedStderr: DISCARD
