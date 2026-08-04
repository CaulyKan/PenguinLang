# ForLetMutWithTypeRejected
## Description
The for-loop variable declaration follows the same rule as ordinary `let`: `for (let mut x : T in ...)` is rejected; use `for (let x : mut T in ...)` (mut in the type) or `for (let mut x in ...)` (inferred). Locks in the for-loop syntax unification.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut _utils.List<i64> = new _utils.List<i64>();
        a.push(1);
        for (let mut x : i64 in a.iter()) {
            print(cast<string>(x));
        }
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot use 'let mut'`
