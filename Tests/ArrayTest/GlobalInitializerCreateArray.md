# GlobalInitializerCreateArray
## Description
Constructing a native array in a file-top-level global initializer.
DOUBLE RED SENTINEL: EmperorPenguin lowers the initializer's
create_array to the UNINSTANTIATED bare symbol `@__builtin_create_array`
(undefined at link); BabyPenguin fails its own type check with
`Cannot assign type mut array<S> to type mut array<S>` (self-assignment
paradox in the global-init assignability path). Each compiler carries an
independent defect on this shape; both should turn green — the working
shape today is a factory function (see tb_real / ArrayTest green cases).

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class S { impl IReferenceType; v: mut i64 = 0; }
let g: mut array<S> = create_array<S>(4, new S());
initial {
    println("n=" + cast<string>(g.size()));
}
```

## Run
ExitCode: 0
ExpectedStdout: EQUALS `n=4
`
