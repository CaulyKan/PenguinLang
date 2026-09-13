# WaitIntShortFormInterleave
## Description
Two initial routines use the short `wait <n>;` timer form with different tick counts; like TimerAdvance, the shorter timer fires first and `_sim_now()` confirms the clock advanced to each deadline (the short form must register real timers, not evaluate-and-discard).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    wait 3;
    println("c:" + cast<string>(_sim_now()));
}
initial {
    wait 1;
    println("a:" + cast<string>(_sim_now()));
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
ExpectedStdout: EQUALS `a:1
c:3
`
ExpectedStderr: DISCARD
