# ArrayBareNameRejected
## Description
Bare `array` (no element type argument) must be rejected — it is a reserved type-constructor name and the error must say so (`array<T> requires an element type argument`). Both compilers emit the identical message at type resolution.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : array = create_array(2, 1);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
