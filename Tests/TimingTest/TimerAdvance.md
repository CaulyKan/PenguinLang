# TimerAdvance
## Description
Two jobs wait different tick counts. The shorter timer fires first and _sim_now() confirms tick advancement.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    wait 3 tick;
    println("c:" + cast<string>(_sim_now()));
}
initial {
    wait 1 tick;
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
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a:1
c:3
`
ExpectedStderr: DISCARD