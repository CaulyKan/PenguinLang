# ArrayImmutableSetRejected
## Description
`set(i, v)` on an immutable `!mut array<T>` binding must be REJECTED at compile time — the approved native-array design says "set(i, v)（越界无操作，需 mut）": writing through an immutable array reference is a mutability violation, exactly like calling a `mut this` method on an immutable receiver. RED SENTINEL — known-buggy on ALL FOUR compilers today (verified 2026-10-17): the array magic-method call rewrites to `__array_set_checked(a, i, v)` and the generic-inference path unifies the receiver with the `mut array<T>` parameter ELEMENT-wise only, never comparing the receiver's own mutability, so the call binds and the write lands. Fix belongs in `rewrite_array_member_call` (EmperorPenguin/src/bound/SemanticBindExpressions.penguin — it already probes the receiver type; `set`/`iter_mut` with an Immutable receiver should report E_TYPE_MISMATCH, mirroring the mut-this method check) and in BabyPenguin's array member resolution (`ICodeContainer.cs` ResolveArrayBuiltinMethod path). Should turn green everywhere once enforced.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : !mut array<i32> = create_array(2, 1);
    a.set(0, 9);
    println("wrote=" + cast<string>(a.at(0).some));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
