# ICopyExplicitDepth

## Description

An explicit `impl ICopy<Self>;` WITHOUT a user-provided `copy` function on a
class that contains reference-typed fields must be REJECTED at compile time.

Rationale: the bodyless impl marker makes `.copy()` lower to the runtime's
shallow clone (`std/c/core_builtin.c:_emperor_ICopy_copy` memcpy /
BabyPenguin CS backend `GlobalState.Clone`), which SHARES the referenced
objects between the original and the copy — silently aliasing mutable state.
The two compiler families also disagreed on the depth (the VM cloned
recursively, the native backends memcpy'd), so no single stdout could satisfy
both. The ICopy contract now requires an explicit `fun copy(this) -> Self`
whenever the class (transitively) contains reference types; a pure value
class keeps the auto-generated (byte-copy) `copy`.

Verified on BabyPenguin (VM), BabyPenguin CS, EmperorPenguin Pass1/Pass2/Pass3:
compilation fails with `error[E_INTERFACE_IMPL]` ("implements ICopy without
providing a 'copy' function ... shallow-copy the shared reference"). See the
companion ICopyExplicitCopyMethod for the accepted spelling (a user-written
`copy` method).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Inner {
    impl IReferenceType;
    x: i64 = 0;
}
class Outer {
    impl ICopy<Self>;
    i: Inner;
}
initial {
    let a : mut Outer = new Outer();
    a.i = new Inner();
    let b : mut Outer = a.copy();
    b.i.x = 7;
    print(cast<string>(a.i.x));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
