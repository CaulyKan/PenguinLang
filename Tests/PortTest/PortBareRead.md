# PortBareRead
## Description
A bare port read in expression position yields the channel's CURRENT slot (the payload value): `let v : i64 = s.out;` and `cast<string>(s.out)` work on the payload type, not the channel object.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Source {
    output out : i64;
    initial {
        this.out.write(7);
    }
}

construct {
    let s : mut Source = new Source();
}

initial {
    let v : i64 = wait s.out;
    println(cast<string>(v));
    let w : i64 = s.out;
    println(cast<string>(w));
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
ExpectedStdout: EQUALS `7
7
`
ExpectedStderr: DISCARD
