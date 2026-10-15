# FreeMtPureYield
## Description
In the free form, negative `wait` is a pure yield (the current coroutine stays runnable in the same convergence round) and `wait 0;` parks for exactly one round — the M0 forms without any clock meaning. A ping-pong between two coroutines driven only by yields converges deterministically: the loop runs a fixed 10 iterations and prints a fixed interleaving (the scheduler's round order for same-priority runnable coroutines is the run-queue FIFO within a round).


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun pinger(n: i64) -> i64 {
    let i : mut i64 = 0;
    while (i < n) {
        wait -1;
        i = i + 1;
    }
    return i;
}

initial {
    let a = async pinger(10);
    let b = async pinger(10);
    let ra : i64 = wait a;
    let rb : i64 = wait b;
    println("done " + cast<string>(ra + rb));
    exit(0);
}
```

## Compile
Args: `--enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 20
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 20
`
ExpectedStderr: DISCARD
