# CatchInLoop
## Description
try/catch inside a loop body: one iteration raises and is caught, the loop continues with subsequent iterations.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): language-level try/catch is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
initial {
    let i : mut i64 = 0;
    while (i < 3) {
        try {
            if (i == 1) panic("mid");
            println(cast<string>(i));
        } catch (e) {
            println("caught " + cast<string>(i));
        }
        i = i + 1;
    }
    println("done");
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
panic: mid
caught 1
2
done
`
ExpectedStderr: DISCARD
