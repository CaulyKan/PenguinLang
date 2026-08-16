# LetInferredNewImmutableMutCallRejected
## Description
`let a = new _utils.List<i64>()` infers an IMMUTABLE binding holding an IMMUTABLE-typed value (the initializer's mutability is NOT inherited under the new semantics). Calling a `mut this` method (`push`) on it must be rejected. Green on all compilers — BabyPenguin reports "Cant use !mut ... as 'this' param", EmperorPenguin "Cannot call method 'push' with 'mut this' on an immutable binding" (common substring E_TYPE_MISMATCH).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a = new _utils.List<i64>();
        a.push(1);
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
