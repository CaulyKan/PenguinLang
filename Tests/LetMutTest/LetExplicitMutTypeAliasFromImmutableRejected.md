# LetExplicitMutTypeAliasFromImmutableRejected
## Description
Explicit annotation `let b: mut _utils.List<i64> = a` where `a` is an IMMUTABLE binding must be rejected: an IReferenceType value aliases, so imm→mut would allow mutating shared state. BabyPenguin-only for now — its `_utils.List` implements `IReferenceType`, so it reports E_MUTABILITY ("Cant assign immutable symbol ... to mutable symbol"). EmperorPenguin's `utils.penguin` `_utils.List` does NOT implement `IReferenceType`, so its imm→mut alias check does not fire and the program compiles there; the check only triggers for classes marked `impl IReferenceType` (see ValueTypeTest/ReferenceTypeRejectsImmToMut). Should be added to EmperorPenguin once `utils.penguin`'s List declares `IReferenceType`.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a = new _utils.List<i64>();
        let b: mut _utils.List<i64> = a;
        println("done");
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_MUTABILITY`
