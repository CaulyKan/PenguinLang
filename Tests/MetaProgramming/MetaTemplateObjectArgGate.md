# MetaTemplateObjectArgGate
## Description
NEGATIVE (R10 gate): an object value-template argument whose #fun returns a type that does NOT implement IUniqueMangleName must be a COMPILE ERROR — without a canonical get_unique_name the specialization would have to mangle by the raw address (unstable across compilations; two equal values would dedup incorrectly). `#fun make_thing() -> Thing` returns a user class with no IUniqueMangleName impl, so `new Bag<#make_thing()>()` fails with E_UNSUPPORTED naming IUniqueMangleName. (Originally used `Box<i64>`, which also has no impl — a #specializing-injected impl would pass the gate but is not callable from the meta runtime, see MetaMangleObjectListArg; a plain user class keeps this test about the gate itself.) Compile exit NONZERO on Pass3 (JIT-only gate).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Thing {
    n: i64;
    fun new(mut this, n: i64) {
        this.n = n;
    }
}
#fun make_thing() -> Thing {
    return new Thing(5);
}
#template(B: Thing)
class Bag {
    n: i64 = 0;
}
initial {
    let a = new Bag<#make_thing()>();
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
