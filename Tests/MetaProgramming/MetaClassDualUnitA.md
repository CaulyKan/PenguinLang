# MetaClassDualUnitA
## Description
Phase 6 v3 dual-unit `#class`: `#class Acc` is BOTH a unit-B meta data structure (a `#fun` instantiates it at compile time — `#use_class(21)` → 42) AND a real runtime class in unit A (initial constructs `new Acc(5)`, calls `add(2)`/`get()` → 7). Guards the `collect_resolved_definitions` unwrap of `meta_class_def` → `class_def`; unit-B collection (collect_meta_class_defs in the prepass, before the rewrite) must keep working. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#class Acc {
    total: mut i64 = 0;
    fun new(mut this, start: i64) { this.total = start; }
    fun add(mut this, x: i64) { this.total = this.total + x; }
    fun get(this) -> i64 { return this.total; }
}
#fun use_class(n: i64) -> i64 {
    let a: mut Acc = new Acc(n);
    a.add(n);
    return a.get();
}
initial {
    let r: mut Acc = new Acc(5);
    r.add(2);
    println("compile=" + cast<string>(#use_class(21)) + " runtime=" + cast<string>(r.get()));
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
ExpectedStdout: EQUALS `compile=42 runtime=7
`
ExpectedStderr: DISCARD
