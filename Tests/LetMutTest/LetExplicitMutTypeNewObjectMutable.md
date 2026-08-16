# LetExplicitMutTypeNewObjectMutable
## Description
`let a: mut _utils.List<i64> = new _utils.List<i64>()` is an IMMUTABLE binding holding a MUTABLE-typed value — calling `mut this` methods (push) is allowed on the mutable value. Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a: mut _utils.List<i64> = new _utils.List<i64>();
        a.push(1);
        a.push(2);
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
ExpectedStdout: EQUALS `2
`
ExpectedStderr: DISCARD
