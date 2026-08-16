# UsingRequired
## Description
A namespace's symbols are NOT visible unqualified without a `using` — unqualified lookup does not scan all namespaces (matches BabyPenguin semantics). This program must fail to compile because `answer()` is only reachable as `helpers.answer()`. BabyPenguin already enforces this; EmperorPenguin aligned with it when the global child-namespace scan was replaced by the imports mechanism.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
namespace helpers {
    fun answer() -> i64 { return 42; }
}
initial {
    println("answer=" + cast<string>(answer()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
