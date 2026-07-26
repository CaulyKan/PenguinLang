# MetaWhileDivergence
## Description
Phase 5a: `#while (true)` without any termination mechanism hits the iteration safety cap and emits a compile error. The test must fail compilation.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#while (true) {
    fun f() -> i64 { return 1; }
}
initial {
    println("unreachable");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
