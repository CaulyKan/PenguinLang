# MtFutureCompletionSameDelta
## Description
`wait f` on a spawn future is a CONDITION wait under the M0 semantics (design doc 4.7/8.3): the child's completion wakes the parent IN THE SAME DELTA — no per-delta polling round. The classic scheduler parks the parent one round (it would print `parent got 7 d2`); both new-semantics schedulers agree on d1: the native mt kernel kicks futures on coroutine completion (mt_run_one status 0), and BabyPenguin's aligned C# scheduler (SimScheduler) does the same in JobFinished. Dual-pinned to keep the reference implementation and the native kernel byte-identical on the wake discipline.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
async fun work() -> i64 {
    println("child d" + cast<string>(__builtin._sim_delta()));
    return 7;
}
initial {
    let f : mut IFuture<i64> = async work();
    let v : i64 = wait f;
    println("parent got " + cast<string>(v) + " d" + cast<string>(__builtin._sim_delta()));
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
ExpectedStdout: EQUALS `child d1
parent got 7 d1
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `child d1
parent got 7 d1
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `child d1
parent got 7 d1
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `child d1
parent got 7 d1
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `child d1
parent got 7 d1
`
ExpectedStderr: DISCARD
