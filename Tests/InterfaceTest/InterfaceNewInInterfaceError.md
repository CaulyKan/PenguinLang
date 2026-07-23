# InterfaceNewInInterfaceError
## Description
Compile error: `fun new(mut this)` is not allowed in interface definitions. The `new` keyword is reserved for class/enum constructors. EmperorPenguin allows it; BabyPenguin rejects it.

## Apply To
* BabyPenguin
* BabyPenguin CS

## Test Code
```
    interface IFoo {
        fun new(mut this) {}
    }
    initial {}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_DUPLICATE_SYMBOL`
