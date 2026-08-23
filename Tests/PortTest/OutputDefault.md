# OutputDefault
## Description
An output port's declared default (`output line : bool = true;`, the UART idle-level case) is written into the fan-out hub as the initial delivery: the module's own bare read sees it, an outside transaction wait wakes immediately with it, and an outside bare read samples it.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Uart {
    output line : bool = true;
    initial {
        println(cast<string>(this.line));
    }
}

construct {
    let u : mut Uart = new Uart();
}

initial {
    let v : bool = wait u.line;
    println(cast<string>(v));
    println(cast<string>(u.line));
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
ExpectedStdout: EQUALS `true
true
true
`
ExpectedStderr: DISCARD
