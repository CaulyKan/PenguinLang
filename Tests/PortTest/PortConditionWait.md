# PortConditionWait
## Description
Level-sensitive wait on a port condition (`wait s.line == false`, the Verilog wait() idiom): the condition re-evaluates every scheduler round through the bare-read slot, so the waiter wakes when the level actually changes without burning a polling process.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Src {
    output line : bool = true;
    initial {
        wait 2 tick;
        this.line.write(false);
    }
}

construct {
    let s : mut Src = new Src();
}

initial {
    wait s.line == false;
    println("low seen");
    exit(0);
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
ExpectedStdout: EQUALS `low seen
`
ExpectedStderr: DISCARD
