# BareWaitLoopSurvives
## Description
A pure bare-wait counting loop keeps making forward motion (its counter changes every round), so the quiescence detector must NOT kill it — the loop runs to completion. This is the regression sentinel for snapshot-based quiescence: identical instruction sequences with mutating registers are alive; only truly identical replays are quiescent.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    let i : mut i64 = 0;
    while (i < 5) {
        i += 1;
        wait;
    }
    println("counted " + cast<string>(i));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `counted 5
`
ExpectedStderr: DISCARD
