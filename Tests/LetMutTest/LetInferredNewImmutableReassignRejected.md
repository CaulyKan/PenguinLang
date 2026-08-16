# LetInferredNewImmutableReassignRejected
## Description
`let a = new _utils.List<i64>()` infers an IMMUTABLE binding, so reassigning `a` must be rejected. Green on all compilers — BabyPenguin reports "Cant assign to immutable symbol", EmperorPenguin "Cannot assign to immutable variable 'a'" (common substring E_MUTABILITY).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a = new _utils.List<i64>();
        a = new _utils.List<i64>();
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_MUTABILITY`
