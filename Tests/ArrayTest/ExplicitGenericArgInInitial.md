# ExplicitGenericArgInInitial
## Description
Calling create_array with an EXPLICIT generic argument inside `initial`.
DOUBLE RED SENTINEL: EmperorPenguin lowers the call to the bare
uninstantiated `@__builtin_create_array` (the initial-routine binding
context skips template instantiation); BabyPenguin does not parse the
explicit-argument call syntax (`Cant resolve symbol 'create_array<S>'`).
Without the explicit argument the same code is green on both — the
defect is the explicit-arg path specifically.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class S { impl IReferenceType; v: mut i64 = 0; }
let g: mut __builtin.Option<mut array<S>> = new __builtin.Option<mut array<S>>.none();
initial {
    g = new __builtin.Option<mut array<S>>.some(create_array<S>(4, new S()));
    println("n=" + cast<string>(g.some.size()));
}
```

## Run
ExitCode: 0
ExpectedStdout: EQUALS `n=4
`
