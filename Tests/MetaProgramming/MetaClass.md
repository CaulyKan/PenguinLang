# MetaClass
## Description
Phase 6 v2 `#class`: a meta-only, full-capability class (`Acc`, with a mutable field + methods) declared in unit A, routed into unit B, and used by a `#fun` (`use_class`) which instantiates it, calls its methods, and returns an i64. `#class` is NOT emitted to runtime — it lives only in unit B for `#fun`s to use as a compile-time data structure. `#use_class(21)` → `Acc.total = 21 + 21 = 42`. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#class Acc {
    total: mut i64;
    fun new(mut this) { this.total = 0; }
    fun add(mut this, x: i64) { this.total = this.total + x; }
    fun get(this) -> i64 { return this.total; }
}
#fun use_class(n: i64) -> i64 {
    let a: mut Acc = new Acc();
    a.add(n);
    a.add(n);
    return a.get();
}
initial {
    println("result=" + cast<string>(#use_class(21)));
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
ExpectedStdout: EQUALS `result=42
`
ExpectedStderr: DISCARD
