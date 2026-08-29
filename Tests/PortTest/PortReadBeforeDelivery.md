# PortReadBeforeDelivery
## Description
A bare read of a never-driven output port returns the payload type's zero value deterministically (design Q3: a port's initial value is its explicit default or the type zero) — no runtime error, no empty payload leak. Updated with the port-initial-value semantics: this previously asserted an error-100 "port read before any value was delivered", which the challenge sentinel PortBareReadDefaultZero supersedes.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class M {
    output y : i64;
    initial {
        println("a");
    }
}

construct {
    let m : mut M = new M();
}

initial {
    let v : i64 = m.y;
    println(cast<string>(v));
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
ExpectedStdout: EQUALS `a
0
`
ExpectedStderr: DISCARD
