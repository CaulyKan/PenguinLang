# WaitConditionNeverTrue
## Description
A condition that never becomes true ends the program at quiescence: the parked waiter contributes identical rounds (no progress, no activity), so the scheduler terminates normally — same rule as every other blocked waiter (waiting is a legal final state, not an error).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): level-sensitive `wait <condition>` is implemented in BabyPenguin only — EmperorPenguin fails here (or no-ops) and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

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
Args: ``
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
