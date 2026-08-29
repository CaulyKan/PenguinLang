# PortBareRead
## Description
A bare port read in expression position yields the channel's CURRENT slot (the payload value): `let v : i64 = s.out;` and `cast<string>(s.out)` work on the payload type, not the channel object.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine`
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
