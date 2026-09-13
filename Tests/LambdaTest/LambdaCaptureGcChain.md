# LambdaCaptureGcChain
## Description
GC safety of escaping closures that capture REFERENCE types: the lambda captures a user-class object holding a reference chain (`Payload -> Payload`), is returned out of the defining frame (the closure object's capture field is then the ONLY reference to the chain), survives 200k intervening heap allocations that force garbage collections, and is finally called — it must observe the captured chain intact. On EmperorPenguin this locks that the synthesized `__lambda_<n>` closure class's capture fields are in the class refmap and traced by the GC; a missed field would sweep the chain and the call would read freed memory.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Payload {
    impl IReferenceType;
    tag: string = "";
    next: mut Option<Payload> = new Option<Payload>.none();
    fun new(mut this, tag: string) { this.tag = tag; }
}
fun make_reader() -> fun<string> {
    let head: mut Payload = new Payload("head");
    let tail: mut Payload = new Payload("tail");
    head.next = new Option<Payload>.some(tail);
    return fun () -> string { return head.tag + ">" + head.next.some.tag; };
}
initial {
    let f: fun<string> = make_reader();
    let i: mut i64 = 0;
    while (i < 200000) {
        let garbage: mut StringBuilder = new StringBuilder();
        garbage.append("x");
        i = i + 1;
    }
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
ExpectedStdout: EQUALS `head>tail
`
ExpectedStderr: DISCARD
