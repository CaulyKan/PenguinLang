# StringFieldAutoClassification

## Description

Auto value/reference classification: a `string` field must make a class
REFERENCE-like on every compiler.

A class with no explicit `IValueType`/`IReferenceType` marker is auto-classified
by walking its fields. `string` is a REFERENCE type that implements ICopy
(spec table in `docs/en/specifications/03_DataTypes.md`; it lowers to a
GC-managed `ref<string>`), so `StrBox` auto-implements `__builtin.IReferenceType`
and assignment aliases — mutating `b.s` is visible through `a` (prints `world`).

Both compilers now agree: BabyPenguin
(`BabyPenguin/SemanticPass/05_InterfaceImplementation.cs`, `IsTypeValueLike` —
string never value-like) and EmperorPenguin
(`EmperorPenguin/src/bound/SemanticClassifyValueTypes.penguin`,
`is_type_value_like` — `PrimitiveType.StringType` excluded since 2026-09-20;
it previously treated every PrimitiveKind as value-like, which made `StrBox`
an inline struct whose assignment copied). Green on all backends;
`string`'s own `.copy()` stays identity through the primitive path (strings
are immutable, sharing is observationally safe).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class StrBox {
    s: string;
}
initial {
    let a : mut StrBox = new StrBox();
    a.s = "hello";
    let b : mut StrBox = a;
    b.s = "world";
    print(a.s);
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
ExpectedStdout: EQUALS `world`
ExpectedStderr: DISCARD
