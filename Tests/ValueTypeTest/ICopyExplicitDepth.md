# ICopyExplicitDepth

## Description

Cross-compiler divergence in `.copy()` DEPTH for a class with an explicit
`impl ICopy<Self>` whose field is a reference type.

The spec (`docs/en/specifications/03_DataTypes.md` §`ICopy<T>`) says a
reference type implementing `ICopy<T>` "must provide its own deep-copy logic";
neither compiler currently accepts a copy method body (the interface method is
declared `extern`), so the bodyless `impl ICopy<Self>;` marker is the only
surface — and the two compilers give it opposite depths:

- BabyPenguin: every `ICopy<T>` extern is backed by a recursive clone
  (VM: `VirtualMachine/ExternFunctions.AddCopy` → `ReferenceRuntimeValue.Clone`;
  CS backend: `CSharpBackend/ExternLowerer.ICopyBody` → `GlobalState.Clone`)
  → `b.i` is a FRESH `Inner`, `b.i.x = 7` is invisible through `a` (prints 0).
- EmperorPenguin: an explicit impl resolves through the interface path to
  `std/c/core_builtin.c:_emperor_ICopy_copy`, a SHALLOW memcpy of
  `metadata->instance_size` bytes → `b.i` still points at the SAME `Inner`
  (prints 7).

Green on BabyPenguin, intentionally red on EmperorPenguin Pass1/2/3 until the
semantics converge — either EmperorPenguin learns to honor custom copy logic
(currently `impl ICopy for Y { fun copy(...) }` bodies fail with
E_RESOLVE_SYMBOL on `this`) and deepens `_emperor_ICopy_copy`, or the spec is
amended to the shallow reading and BabyPenguin matches it. Note the shallow
reading would also change the auto-generated copies BabyPenguin performs at
every value boundary for nested reference-typed fields.

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
    print("a.i.x=");
    print(cast<string>(a.i.x));
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
ExpectedStdout: EQUALS `a.i.x=0`
ExpectedStderr: DISCARD
