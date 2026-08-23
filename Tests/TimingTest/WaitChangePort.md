# WaitChangePort
## Description
`wait change(x)` on a PORT expression (the UART line-watching shape): the watched value is re-sampled through the port's slot every scheduler round, catching both edges of true -> false -> true and returning each new level.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the wait change(x) edge-detection sugar is implemented in BabyPenguin only — EmperorPenguin resolves `change` as an unresolved call and fails there; it should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Src {
    output line : bool = true;
    initial {
        wait 2 tick;
        this.line.write(false);
        wait 2 tick;
        this.line.write(true);
    }
}

construct {
    let s : mut Src = new Src();
}

initial {
    wait change(s.line);
    println("edge1");
    let v : bool = wait change(s.line);
    println(cast<string>(v));
    exit(0);
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
ExpectedStdout: EQUALS `edge1
true
`
ExpectedStderr: DISCARD
