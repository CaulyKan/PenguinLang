# GenericNewInGenericMethod
## Description
RED SENTINEL (expected to fail on Pass2/Pass3 until fixed). Transitive generic specialization gap: when a generic class's METHOD BODY instantiates ANOTHER generic class with the outer class's own type parameter — `Holder<T>.use() { let w = new Wrap<T>(5); return w.doubled(); }` — the inner `Wrap<T>` is never specialized. Root cause: `SemanticModel.collect_instantiations_from_def` only collects generic instantiations from type SIGNATURES (fields / return / param types), not from method BODIES; and `collect_generic_instantiations_from_ast` scans the TEMPLATE body where `T` is still an unresolved placeholder, so `new Wrap<T>` can't be resolved to `Wrap<i32>`. Monomorphization converges in 1 iteration without ever specializing `Wrap__i32`, so `emit_new` finds no layout for it (`; NEW repro.Wrap__i32 (no layout)`), emits no allocation, and the result register is undefined (`error: use of undefined value '%t2'`). This is the same gap that blocks `Array<T,N>` from using a `_ptr<T>` helper internally (it must inline `#__load`/`#__store` instead). Should turn green once monomorphization scans specialized method bodies for inner instantiations (or binds bodies before the collect pass). Verified red on Pass2 and Pass3 (EmperorPenguin).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace repro {
    #template(T: type)
    class Wrap {
        v: mut i64;
        fun new(mut this, a: i64) { this.v = a; }
        fun doubled(this) -> i64 { return this.v * 2; }
    }
    #template(T: type)
    class Holder {
        fun use(this) -> i64 {
            let w = new Wrap<T>(5);
            return w.doubled();
        }
    }
}
initial {
    let h = new repro.Holder<i32>();
    println("result=" + cast<string>(h.use()));
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
ExpectedStdout: EQUALS `result=10
`
ExpectedStderr: DISCARD
