# TickInterleave
## Description
Two initial routines wait different tick counts. The shorter timer fires first, proving tick-based ordering.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    wait 1 tick;
    println("A");
}
initial {
    wait 2 tick;
    println("B");
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
ExpectedStdout: EQUALS `A
B
`
ExpectedStderr: DISCARD
