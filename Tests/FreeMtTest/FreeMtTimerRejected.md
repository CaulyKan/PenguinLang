# FreeMtTimerRejected
## Description
The free-form scheduler has NO virtual clock: `wait n;` with n > 0 has no tick to schedule on. The lowering still routes to _mt_wait_ticks (same as sim-mt), whose free-form implementation reports and exits 3 — loudly, instead of parking forever on a timer that can never fire. Negative `wait` stays a pure yield and `wait 0;` still parks one convergence round (both exercised by FreeMtPureYield).


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("before");
    wait 5;
    println("unreachable");
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
ExpectedExitCode: 3
ExpectedStdout: EQUALS `before
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 3
ExpectedStdout: EQUALS `before
`
ExpectedStderr: DISCARD
