# GenericClassShadowedByBuiltin

## Description

Regression sentinel for the `use of undefined value '%t0'` bug in EmperorPenguin's
type resolution.

When a user-defined generic class in a namespace shares its simple name with a
`__builtin` generic class (here `test.Box` vs `__builtin.Box`), an unqualified
`new Box<i32>()` is resolved by `BoundScope.lookup_type_in_scope`, which at the
global scope walks child namespaces in insertion order and returns
`__builtin.Box` before the user's `test.Box`. The `new` is therefore bound to the
wrong type, `test.Box__i32` is never monomorphized, `LLVMEmitter.emit_new` finds
no class layout, emits only a `; NEW ... (no layout)` comment, and the result SSA
register (`%t0`) is never defined — so the later `#dbg_value(ptr %t0, ...)` fails
clang with `error: use of undefined value '%t0'`.

This is a valid program: the C# reference compiler (BabyPenguin) compiles and
runs it correctly, printing `ok`. EmperorPenguin Pass1/2/3 currently FAIL it at
the link stage. This test is intentionally a red sentinel on EmperorPenguin — it
should turn green once `lookup_type_in_scope` is made scope-aware (prefer the
current namespace chain) or `mangle_generic_name` uses qualified names. When that
happens, no change is needed here beyond confirming all four compilers agree.

Root cause is in EmperorPenguin's own sources:
- `EmperorPenguin/src/bound/BoundScope.penguin` (`lookup_type_in_scope`, global
  child-namespace search order)
- `EmperorPenguin/src/bound/SemanticModel.penguin` (`bind_new_expr` →
  `resolve_type_by_name` → `lookup_type_in_scope`)

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace test {
    #template(T: type)
    class Box {
        value: T;
    }
    initial {
        let b = new Box<i32>();
        println("ok");
    }
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
ExpectedStdout: EQUALS `ok
`
ExpectedStderr: DISCARD
