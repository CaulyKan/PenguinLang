# LambdaCaptureGcOptionPayload
## Description
GC safety of an escaping closure kept alive ONLY through an `Option<fun<i64, i64>>` enum payload while capturing a reference-type object: `install()` builds a closure over a local `Cell` and stores the fun value into a global Option through a helper (`global_store`) so the defining frame, the helper frame and every local are gone — the only path to both the closure AND the captured Cell is the enum payload. 200k forced collections later, the call must read the captured Cell's field (`100 + 23`). This combines the enum-payload tracing of FunValueGcStress with reference-type capture: the GC must reach the Cell THROUGH closure-field tracing, two heap hops from the payload root.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Cell {
    impl IReferenceType;
    v: mut i64 = 0;
    fun new(mut this, v: i64) { this.v = v; }
}
let keep: mut Option<fun<i64, i64>> = new Option<fun<i64, i64>>.none();
fun install() {
    let cell: mut Cell = new Cell(100);
    let f: fun<i64, i64> = fun (x: i64) -> i64 { return cell.v + x; };
    global_store(f);
}
fun global_store(f: fun<i64, i64>) {
    keep = new Option<fun<i64, i64>>.some(f);
}
initial {
    install();
    let i: mut i64 = 0;
    while (i < 200000) {
        let garbage: mut StringBuilder = new StringBuilder();
        garbage.append("z");
        i = i + 1;
    }
    if (let f := keep.some) {
        println(cast<string>(f(23)));
    }
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
ExpectedStdout: EQUALS `123
`
ExpectedStderr: DISCARD
