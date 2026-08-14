# MetaTemplateMixedTypeValueMetaCall
## Description
MIXED args through the shared per-arg resolver: a type arg AND a meta-call VALUE arg in one instantiation — `P<i32, #compute_n()>` for `#template(T: type, N: i32)`. resolve_single_generic_arg dispatches per position (type → type arg; #f() → JIT-evaluated int value arg), the specialization P__i32__5 substitutes N=5 into the field and keeps T=i32 for the method signature (`ident(x: T) -> T` round-trips 7). Compile exit 0, output v=5,id=7. Pass3 (meta-call value args need the JIT).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun compute_n() -> i64 {
    return 5;
}
#template(T: type, N: i32)
class P {
    v: i32 = N;
    fun ident(this, x: T) -> T {
        return x;
    }
}
initial {
    let p = new P<i32, #compute_n()>();
    let r: i32 = p.ident(7);
    println("v=" + cast<string>(p.v) + ",id=" + cast<string>(r));
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
ExpectedStdout: EQUALS `v=5,id=7
`
ExpectedStderr: DISCARD
