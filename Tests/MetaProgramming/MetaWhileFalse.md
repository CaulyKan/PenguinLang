# MetaWhileFalse
## Description
Phase 5a: definition-level `#while (false)` drops the body entirely. The `dropped` function should not exist — attempting to call it is a compile error.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#while (false) {
    fun dropped() -> i64 { return 1; }
}
initial {
    println(cast<string>(dropped()));  // ERROR: dropped is not defined
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
