# PrimitiveIHashDispatch
## Description
End-to-end test of the primitive interface-method dispatch (Stage 1): `impl IHash for i64/string/bool` methods are mangled to `hash$$i64` etc. and `k.hash()` on a primitive base resolves to a direct call. i64 returns itself; bool maps true/false to 1/0; string runs FNV-1a over its characters (fixed output for "hello"). Verified on the native pass2/pass3 compilers (BabyPenguin has no `hash()`-style dispatch for these yet and is not listed).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let k: i64 = 42;
    println("i64=" + cast<string>(k.hash()));
    let s: string = "hello";
    println("string=" + cast<string>(s.hash()));
    let b: bool = true;
    println("bool_true=" + cast<string>(b.hash()));
    let b2: bool = false;
    println("bool_false=" + cast<string>(b2.hash()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `i64=42
string=2607821981565500683
bool_true=1
bool_false=0
`
ExpectedStderr: DISCARD
