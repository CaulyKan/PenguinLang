# MetaIfExclude
## Description
Phase 5a negative test: a `#if (false)` block is dropped entirely, so `ghost()` never enters the compilation. Calling it must fail to compile with an unresolved-symbol error.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#if (false) {
    fun ghost() -> i64 { return 99; }
}
initial {
    println(cast<string>(ghost()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `ghost`
