# MetaDefineRedefineError
## Description
File-scoped define semantics (batch 2.1/2.2, both implementations): redefining the same key within ONE file by code is E_DUPLICATE_SYMBOL — the compile fails with a diagnostic naming the key. Overwriting a CLI --define seed is NOT a redefinition (code wins silently) — see MetaCliDefinePriority.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("MODE","1");
#define("MODE","2");
initial {
    println("unreachable");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `MODE`
