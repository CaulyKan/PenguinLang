# ExitCodeWhileParked
## Description
exit(code) from inside a coroutine with a NONZERO code while another coroutine sits parked on a timer wait: the park switch must land back in the scheduler with the exit status, the loop must read the exit code, and the cleanup path must free the timer-parked coroutine's stack (no double-destroy, no hang) before main returns the code. Locks in the status-3 exit plumbing of the Penguin-side scheduler loop (the `_co_switch_in` → `_sched_exit_code` → `__sched_run` return path introduced by the scheduler migration).

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("before");
    exit(42);
    println("after");
}
initial {
    wait 5 tick;
    println("never");
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 42
ExpectedStdout: EQUALS `before
`
ExpectedStderr: DISCARD
