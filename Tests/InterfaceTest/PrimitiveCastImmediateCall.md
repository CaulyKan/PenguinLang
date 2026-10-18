# PrimitiveCastImmediateCall
## Description
The interface-cast immediate-call exception: `cast<I>(prim).m(...)` is legal and compiles to a direct call of the primitive's impl — the cast node is rewritten away entirely. The target interface must declare the method; the receiver is the primitive value itself.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a: i64 = 42;
    println(cast<string>(cast<IHash>(a).hash()));
    let s: string = "xy";
    println(cast<string>(cast<IStringOps>(s).length()));
    println(cast<string>(cast<IHash>(a).hash() + 1));
    println(cast<string>(cast<IStringOps>(s).to_upper()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `42
2
43
XY
`
ExpectedStderr: DISCARD
