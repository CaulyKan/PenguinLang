# UnaryPlusOnStringError
## Description
Compile error: unary `+` operator cannot be applied to string type. String concatenation uses binary `+`, but unary `+` is only valid on numeric types.

## Apply To
* BabyPenguin
* BabyPenguin CS

## Test Code
```
    initial {
        println(+"hello");
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
