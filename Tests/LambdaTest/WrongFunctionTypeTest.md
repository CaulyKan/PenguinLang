# WrongFunctionTypeTest
## Description
Compile error: assigning function with return value to fun<void>.

## Apply To
* BabyPenguin

## Test Code
```
    fun x() -> i32 { 
    }
    initial {
        let y : fun<void> = x;
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
