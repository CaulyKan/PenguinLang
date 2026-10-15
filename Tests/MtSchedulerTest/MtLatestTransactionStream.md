# MtLatestTransactionStream
## Description
LatestChannel as a TRANSACTION STREAM (the wire row of design doc §8.2): writes in SEPARATE deltas each become one committed transaction delivered exactly once, in write order, to the single consumer's cursor — 1, 2, 3 across three deltas sum to 6. Complements MtDeferredWire (same-delta collapse): together the two pin the wire contract — one transaction per delta, later deltas queue as distinct transactions ("a late consumer never loses transactions"). The consumer parks between takes (directed wake from the boundary's per-carrier commit_now, batch 3.1).


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun consumer(ch: mut __builtin.LatestChannel<i64>) -> i64 {
    let a : i64 = wait ch;
    let b : i64 = wait ch;
    let c : i64 = wait ch;
    return a + b + c;
}

initial {
    let ch : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    let c = async consumer(ch);
    wait 0;
    ch.write(1);
    wait 0;
    ch.write(2);
    wait 0;
    ch.write(3);
    let total : i64 = wait c;
    println("stream " + cast<string>(total));
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
ExpectedStdout: EQUALS `stream 6
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `stream 6
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `stream 6
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `stream 6
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `stream 6
`
ExpectedStderr: DISCARD
