# LetMutInferredLiftsImmutableReturn
## Description
`let mut a = get_list()` where `get_list()` returns a non-mut `_utils.List<i64>` — the inferred `let mut` binding UNCONDITIONALLY lifts the type to mutable (the initializer's own mutability is not inherited), so `push` works. This locks in the semantic used by macro-generated code (e.g. `let mut _map = _f.as_object()`). Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun get_list() -> _utils.List<i64> {
        return new _utils.List<i64>();
    }
    initial {
        let mut a = get_list();
        a.push(7);
        println(cast<string>(a.size()));
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1
`
ExpectedStderr: DISCARD
