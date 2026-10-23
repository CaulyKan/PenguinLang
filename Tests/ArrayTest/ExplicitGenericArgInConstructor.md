# ExplicitGenericArgInConstructor
## Description
Calling create_array with an EXPLICIT generic argument inside a class
CONSTRUCTOR. DOUBLE RED SENTINEL, sibling of
ExplicitGenericArgInInitial.md (same defect family, different binding
context): EmperorPenguin lowers the call to the bare uninstantiated
`@__builtin_create_array` (the constructor binding context also skips
template instantiation — the link fails with `use of undefined value
'@__builtin_create_array'`), and BabyPenguin does not parse the
explicit-argument call syntax at all. Without the explicit argument the
same code is green — the defect is the explicit-arg path in class-method
binding contexts specifically. Found while building the two-tier GC
benchmark suite (Cache wrapper class, 2026-10-23).

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class S { impl IReferenceType; v: mut i64 = 0; }
class Holder {
    impl IReferenceType;
    slots: mut array<S>;
    fun new(mut this, n: u64) {
        this.slots = create_array<S>(n, new S());
    }
    fun count(this) -> i64 {
        return cast<i64>(this.slots.size());
    }
}
initial {
    let h: mut Holder = new Holder(cast<u64>(4));
    println("n=" + cast<string>(h.count()));
}
```

## Run
ExitCode: 0
ExpectedStdout: EQUALS `n=4
`
