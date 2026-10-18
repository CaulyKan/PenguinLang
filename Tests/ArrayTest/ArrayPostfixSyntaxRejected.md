# ArrayPostfixSyntaxRejected
## Description
The retired postfix spelling `mut i32[]` must not parse — `[`/`]` are no longer lexed as type brackets on either compiler (token-recognition / parse error). Locks the syntax migration: no dual-spelling period.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut i32[] = create_array(2, 1);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
