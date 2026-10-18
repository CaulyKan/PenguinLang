# ArrayElementMutabilityRejected
## Description
`array<mut T>` (element-level mutability inside the wrapper) must be rejected with the dedicated message — the mutability prefix on `array<...>` applies to the array reference; elements are immutable. Rejecting outright (instead of silently normalizing) keeps the spelling unambiguous. Identical message on both compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut array<mut i32> = create_array(2, 1);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
