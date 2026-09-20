# GcStructHomeValueClass
## Description
GC v2 cover-retirement smoke: immutable value-class / Option<string> registers (embedded references) promoted onto the struct-home storage model. `EMPEROR_GC_YOUNG=65536` forces a minor collection every 64KB, so the loop churns through dozens of minors while `Option<string>` temps, value-class fields (`Rec` inside `Holder`) and enum payloads cross safepoint polls as SSA values. Before the struct-home promotion these lived only in clang spill slots no frame descriptor could name — the conservative main-stack cover was the only net and the precise mode corrupted. Green on all four GC modes = the frame descriptors fully root the embedded references.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace t {
    class Rec { impl __builtin.IValueType; a: i32; b: string;
        fun new(mut this, a: i32, b: string) { this.a = a; this.b = b; } }
    class Holder { impl __builtin.IReferenceType; r: Rec; name: string;
        fun new(mut this, r: Rec, name: string) { this.r = r; this.name = name; } }
    fun mkopt(s: string) -> Option<string> { return new Option<string>.some(s); }
}
initial {
    let base: string = "base-";
    let acc: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < 200) {
        let s: string = base + cast<string>(i);
        let o = t.mkopt(s);
        let lit = new Option<string>.some(s);
        let r = new t.Rec(i, s);
        let h = new t.Holder(r, s);
        if (o.is_some()) { acc = acc + string_length(o.value_or("")); }
        if (lit.is_some()) { acc = acc + string_length(lit.value_or("")); }
        acc = acc + h.r.a + string_length(h.name);
        i = i + 1;
    }
    println("acc=" + cast<string>(acc));
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
ExpectedStdout: EQUALS `acc=24370
`
ExpectedStderr: DISCARD
