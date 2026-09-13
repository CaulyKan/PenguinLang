# LambdaCaptureGcMutAccumulate
## Description
GC safety of an escaping closure capturing a MUTABLE reference across MULTIPLE calls with collections in between: the lambda captures a `mut StringBuilder` and appends to it on every call (snapshot semantics keep the captured binding mutable, so state accumulates inside the captured object). 200k allocations run between the calls; the captured StringBuilder must survive every collection and the calls must return the growing prefixes `x`, `xx`, `xxx` — a swept capture would return garbage or crash instead.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun make_acc() -> fun<string> {
    let sb: mut StringBuilder = new StringBuilder();
    return fun () -> string { sb.append("x"); return sb.to_string(); };
}
initial {
    let f: fun<string> = make_acc();
    println(f());
    let i: mut i64 = 0;
    while (i < 200000) {
        let garbage: mut StringBuilder = new StringBuilder();
        garbage.append("y");
        i = i + 1;
    }
    println(f());
    println(f());
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
ExpectedStdout: EQUALS `x
xx
xxx
`
ExpectedStderr: DISCARD
