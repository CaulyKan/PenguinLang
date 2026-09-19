# StringFieldAutoClassification

## Description

Cross-compiler divergence in AUTO value/reference classification: does a
`string` field make a class value-like?

A class with no explicit `IValueType`/`IReferenceType` marker is auto-classified
by walking its fields:

- BabyPenguin (`BabyPenguin/SemanticPass/05_InterfaceImplementation.cs`,
  `IsTypeValueLike`): `string` is NOT value-like ("String: reference type, not
  value-like") → `StrBox` auto-implements `__builtin.IReferenceType`, and
  assignment aliases (prints `world`).
- EmperorPenguin (`EmperorPenguin/src/bound/SemanticClassifyValueTypes.penguin`,
  `is_type_value_like`): every `PrimitiveKind` — including string — is
  value-like ("All primitives (including string) are value types") → `StrBox`
  becomes a value class (`#sizeof(StrBox)` == 16, an inline struct), and
  assignment copies (prints `hello`).

The spec table in `docs/en/specifications/03_DataTypes.md` lists `string` under
reference types, which matches BabyPenguin. EmperorPenguin's behavior is a
deliberate in-code choice (strings are immutable `ref<string>`, so shallow-copy
of the pointer is observationally safe for the string itself — but the aliasing
of the CONTAINING object still differs, as this test shows). The two compilers
must agree; until they do, this test is green on BabyPenguin and red on
EmperorPenguin Pass1/2/3, and should turn green the day EmperorPenguin either
excludes string from `is_type_value_like` or the spec is updated to bless the
value-like reading on both compilers.

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
