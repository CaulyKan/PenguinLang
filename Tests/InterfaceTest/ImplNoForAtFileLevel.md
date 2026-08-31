# ImplNoForAtFileLevel
## Description
RED SENTINEL (known bug, kept as a stable failure until fixed): an `impl` block written at FILE level without a `for` clause (`impl IPing { fun ping() ... }`) is silently accepted and then DROPPED — EmperorPenguin exits 0, but the emitted `.ll` contains only dangling calls (`call void @_ns_..._ping()`) with no function definitions, so the downstream `emperor link` fails with undefined symbols. The reference grammar (BabyPenguin, ANTLR parser) REJECTS the form at parse time (`mismatched input '{' expecting 'for'` — an `impl` outside a class body requires `impl Iface for Type { ... }`), so the correct behavior is a compile-time error, never a silent drop. Root cause: the EmperorPenguin Parser accepts the no-`for` impl at top level and BuildScopes/IR emission never surface it. Green on BabyPenguin (E_PARSE); should turn green on EmperorPenguin once it rejects (or meaningfully implements) the file-level no-`for` form.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IPing {
    fun ping() -> string;
}
class A {
    a: i64;
}
impl IPing {
    fun ping() -> string { return "P"; }
}
initial {
    println(A.ping());
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
