# ArrayImmutableThawRejected
## Description
Re-freezing is fine (`mut array<T>` → `!mut array<T>`), but THAWING an immutable array reference back to mutable must be rejected — `let m : mut array<i32> = f;` where `f : !mut array<i32>`, and likewise passing `f` to a `mut array<T>` parameter. BabyPenguin (the C# reference) already rejects this with E_MUTABILITY ("Cant assign immutable symbol ... to mutable symbol"). RED SENTINEL on the EmperorPenguin side (verified 2026-10-17): EP accepts the thaw and the call because its assignment-mutability check is gated on `is_iref_type(value_type)` and an ArrayKind type has no type_definition so it never classifies as IReferenceType; the parameter path goes through `can_implicitly_cast` whose `is_same_type` never compares mutability. Fix: include ArrayKind in the reference-mutability compatibility check (SemanticBindBodies.penguin let/assignment paths) and compare array mutability in argument checking. BabyPenguin stays green; pass1/2/3 turn green once fixed.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun write_first(a: mut array<i32>) {
    a.set(0, 5);
}
initial {
    let f : !mut array<i32> = create_array(2, 1);
    let m : mut array<i32> = f;
    write_first(f);
    println("m=" + cast<string>(m.at(0).some));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
