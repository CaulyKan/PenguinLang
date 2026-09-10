# GcNoStackCoverParamPin

## Description
GC v2 cover-retirement guard: with the conservative main-stack cover OFF (`EMPEROR_GC_NO_STACK_COVER=1`), precise-mode minors must neither free nor MOVE references that live only as SSA/spill copies of PARAMETERS. Exposure classes this locks in (all crashed/corrupted the compiler self-compile before the fix):
(a) bare/qualified-spelled reference parameters (`b: t.Box<string>` — spellings without the `ref<>` wrapper, which the slot classifier used to skip entirely) previously got NO mirror slot at all — the receiver was judged dead mid-call;
(b) scalar ref/string parameters and `this` receivers were mirror-rooted but PROMOTED — the body's SSA uses kept pointing into the recycled chunk after the object moved (mirrors are PIN slots now, pinned before any promotion);
(c) fresh objects in constructor flight were quarantined via evacuation, which likewise MOVED them under SSA-held references (the quarantine pins in place now).
`EMPEROR_GC_YOUNG=65536` forces a minor every 64KB so the churn loops cross dozens of polls while the parameters are live only as SSA copies. Green on Pass2/Pass3 = parameters and in-flight news survive cover-off minors un-moved.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace t {
    #template(T: type)
    class Box {
        impl __builtin.IReferenceType;
        item: T;
        fun new(mut this, item: T) { this.item = item; }
    }
    class Node {
        impl __builtin.IReferenceType;
        v: i64;
        s: string;
        fun new(mut this, v: i64, s: string) { this.v = v; this.s = s; }
    }
    fun boxed_len(b: t.Box<string>, junk: string) -> i64 {
        let acc: mut i64 = string_length(b.item) + string_length(junk);
        let i: mut i64 = 0;
        while (i < 30) {
            let noise: string = junk + cast<string>(i);
            acc = acc + string_length(b.item) + string_length(noise) + 1;
            i = i + 1;
        }
        return acc;
    }
    fun node_probe(n: Node, tag: string) -> i64 {
        let sum: mut i64 = 0;
        let i: mut i64 = 0;
        while (i < 30) {
            sum = sum + n.v + string_length(n.s) + string_length(tag);
            i = i + 1;
        }
        return sum;
    }
}
initial {
    let acc: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < 120) {
        let b = new t.Box<string>("payload-" + cast<string>(i));
        let n = new t.Node(i, "node-" + cast<string>(i));
        acc = acc + t.boxed_len(b, "junk-" + cast<string>(i));
        acc = acc + t.node_probe(n, "tag");
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_NO_STACK_COVER=1 EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `acc=323960
`
ExpectedStderr: DISCARD
