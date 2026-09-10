# GcEnumPayloadChurn
## Description
GC v2 enum-payload stress: 50,000 iterations building `Expr` enums with string payloads, pattern-matching both variants, and concatenating into an accumulator. `EMPEROR_GC_YOUNG=65536` forces a minor on nearly every allocation — each enum crosses polls as an SSA struct whose payload reference the frame descriptor must root, and each promotion must rewrite the payload slot. Exercises the per-tag payload ref-map machinery end to end.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
enum Expr { num: i64; txt: string; }
fun render(e: Expr) -> string {
    let r: mut string = "";
    if (e is Expr.num) { r = "n" + cast<string>(e.num); }
    if (e is Expr.txt) { r = "t" + e.txt; }
    return r;
}
fun build(d: i64) -> string {
    let acc: mut string = "";
    let i: mut i64 = 0;
    while (i < d) {
        let e1 = new Expr.num(i);
        let e2 = new Expr.txt("x");
        acc = acc + render(e1) + render(e2);
        i = i + 1;
    }
    return acc;
}
initial {
    let out = build(50000);
    println("len=" + cast<string>(string_length(out)) + " head=" + string_substring(out, 0, 8));
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `len=388890 head=n0txn1tx
`
ExpectedStderr: DISCARD
