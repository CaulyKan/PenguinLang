# WaitCondition
## Description
`wait <condition>` is a level-sensitive wait: the routine parks and the condition is re-evaluated every scheduler round until it holds (replaces the old `on <expr>` routines).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): level-sensitive `wait <condition>` is implemented in BabyPenguin only — EmperorPenguin fails here (or no-ops) and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let a : mut i32 = 0;

initial {
    wait 2 tick;
    a = 5;
    println("set");
}

initial {
    wait a == 5;
    println("seen");
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
ExpectedStdout: EQUALS `set
seen
`
ExpectedStderr: DISCARD
