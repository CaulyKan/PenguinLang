# TickInLoop
## Description
A for loop waits N ticks per iteration where N varies at runtime; the output sequence confirms incremental tick waits.

## Apply To
* BabyPenguin

## Test Code
```
initial {
    for (let i : i64 in range(0, 3)) {
        wait (i + 1) tick;
        println(cast<string>(i));
    }
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `0
1
2
`
ExpectedStderr: DISCARD