# TryWithWait
## Description
A try region stays active across a suspension: the frame blocks on `wait 2 tick`, resumes inside the try body, and an error raised after resumption is still caught by the same handler.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): language-level try/catch is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
initial {
    try {
        wait 2 tick;
        println("resumed");
        panic("late");
    } catch (e) {
        println("caught: " + e.message);
    }
    println("after");
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
ExpectedStdout: EQUALS `resumed
panic: late
caught: late
after
`
ExpectedStderr: DISCARD
