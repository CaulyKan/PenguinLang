# LetMutWithTypeRejected
## Description
`let mut x : T = v` (mut on the binding AND an explicit type) is rejected on all compilers — the canonical forms are `let x : mut T = v` and `let mut x = v`. This locks in the syntax unification between BabyPenguin and EmperorPenguin.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let mut x : i32 = 0;
        print(cast<string>(x));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Cannot use 'let mut'`
