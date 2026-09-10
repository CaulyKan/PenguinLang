# GcEnumGlobalState
## Description
GC v2: a mutable ENUM global (`cur: mut Shape`) is a value-class global registered as a scan region; `getcur()` returns it by value (struct home) and `setcur` stores a fresh variant. Under a tiny nursery the global's inline payload (the `label` string) must be rewritten in place by every minor — a missed region walk or a bad enum ref-map leaves the payload pointing at recycled nursery memory.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
enum Shape { point: i32; label: string; }
let cur: mut Shape = new Shape.point(3);
fun getcur() -> Shape { return cur; }
fun setcur(s: Shape) { cur = s; }
initial {
    let s1 = getcur();
    setcur(new Shape.label("x"));
    let s2 = getcur();
    println("done");
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
ExpectedStdout: EQUALS `done
`
ExpectedStderr: DISCARD
