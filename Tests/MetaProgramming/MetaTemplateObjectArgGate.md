# MetaTemplateObjectArgGate
## Description
NEGATIVE (R10 gate): an object value-template argument whose #fun returns a type that does NOT implement IUniqueName must be a COMPILE ERROR — without a canonical get_unique_name the specialization would have to mangle by the raw address (unstable across compilations; two equal values would dedup incorrectly). `#fun make_box() -> Box<i64>` returns a stdlib reference type (correct ptr ABI), but Box has no IUniqueName impl, so `new Bag<#make_box()>()` fails with E_UNSUPPORTED naming IUniqueName. Compile exit NONZERO on Pass3 (JIT-only gate).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun make_box() -> Box<i64> {
    let b = new Box<i64>(5);
    return b;
}
#template(B: Box<i64>)
class Bag {
    n: i64 = 0;
}
initial {
    let a = new Bag<#make_box()>();
    println("unreachable");
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
