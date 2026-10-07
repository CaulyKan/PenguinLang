# PortConditionWait
## Description
The Verilog-style level wait `wait s.line == false` is REMOVED (a bool condition has no wake source; concurrency redesign M0). The migrated form consumes port TRANSACTIONS in a loop — `let v = wait s.line; while (v) { v = wait s.line; }` — parking on the channel itself, so the waiter wakes exactly when the low transaction arrives: prints `low seen`.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Src {
    output line : bool = true;
    initial {
        wait 2;
        this.line.write(false);
    }
}

construct {
    let s : mut Src = new Src();
}

initial {
    let v : mut bool = wait s.line;
    while (v) {
        v = wait s.line;
    }
    println("low seen");
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `low seen
`
ExpectedStderr: DISCARD
