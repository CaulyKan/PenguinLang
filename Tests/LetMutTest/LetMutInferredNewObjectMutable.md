# LetMutInferredNewObjectMutable
## Description
`let mut a = new _utils.List<i64>()` infers a MUTABLE binding holding a MUTABLE-typed value, so `push` (a `mut this` method) works. Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let mut a = new _utils.List<i64>();
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
