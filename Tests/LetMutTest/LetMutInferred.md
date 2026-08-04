# LetMutInferred
## Description
The inferred mutable-binding shorthand `let mut x = v` (no type annotation) is reassignable. Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let mut x = 0;
        x = x + 1;
        print(cast<string>(x));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
