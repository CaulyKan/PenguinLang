# WaitConditionNeverTrue
## Description
A condition that never becomes true ends the program at quiescence: the parked waiter contributes identical rounds (no progress, no activity), so the scheduler terminates normally — same rule as every other blocked waiter (waiting is a legal final state, not an error).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let a : mut i32 = 0;

initial {
    println("start");
    wait a == 99;
    println("never");
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `start
`
ExpectedStderr: DISCARD
