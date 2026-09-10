# ImplNoForAtFileLevel
## Description
An `impl` block written at FILE level without a `for` clause (`impl IPing { fun ping() ... }`) is rejected at compile time by every compiler: the reference grammar (BabyPenguin, ANTLR) only allows `interfaceForImplementation` inside class/enum/interface bodies — `namespaceBody` permits `impl Iface for Type { ... }` only (`mismatched input '{' expecting 'for'`). EmperorPenguin used to accept the form and silently DROP it (exit 0) while the emitted `.ll` kept dangling `_ns_..._ping()` calls with no definitions, breaking the downstream `emperor link`; `BuildScopesPass.bind_impl_def` now reports `E_ORPHAN_IMPL` ("file-level 'impl' requires a 'for' clause") when a no-`for` impl reaches a file/namespace scope. The check lives in the semantic layer, NOT the parser, so `compiler().create_definition("impl ... { ... }")` keeps working — meta-generated impls are spliced into class member lists and bind from the class scope (the `#impl_json_serializable()` auto-impl depends on this). Locks in the fix: compile must exit NONZERO, never a silent drop.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
