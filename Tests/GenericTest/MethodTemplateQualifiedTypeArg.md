# MethodTemplateQualifiedTypeArg
## Description
RED SENTINEL (known gap in EmperorPenguin, found during iterator-combinator work): a method-level `#template(C: type)` instantiated with a QUALIFIED type argument (`h.make<outer.Bag2>()`). BabyPenguin does not support method-level templates on non-template classes at all (`Cant resolve return type 'C'`), so this is EP-only semantics. The expression-position parse of qualified type args was fixed (Parser: qualified dot-chains + the missing `<` consume before nested-generic recursion in try_parse_genericTypeArgs — `x.foo<Bar<i64>>()` never parsed before), and the pass-3 bare-name template ambiguity is now skipped in favor of the receiver-aware pass-8 ensure path. REMAINING failure: the specialized body's `new C()` lowers with a `C$<hash>` type name (IRGenerator.lower_new mangles the FUNCTION-SCOPE type-param symbol's full_name instead of the bound type's template def) → class has no layout → emit_new silently skips → `use of undefined value '%t1'` at link. Fix direction: in lower_new's generic_args branch, mangle the bound_type's type_definition full name; NOTE this also exposes a second hole (on-demand spec classes whose constructors are never emitted — previously masked by the silent no-layout skip), so the fix needs both: the mangle base AND ctor emission for ensure-created specs. Should turn green once both land.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace outer {
    class Bag2 {
        impl __builtin.IReferenceType;
        count: mut i64 = 0;
        fun push(mut this, v: i64) { this.count = this.count + 1; }
    }
    class Holder {
        impl __builtin.IReferenceType;
        #template(C: type)
        fun make(mut this) -> C {
            let mut c = new C();
            c.push(1);
            return c;
        }
    }
}
initial {
    let mut h = new outer.Holder();
    let b: outer.Bag2 = h.make<outer.Bag2>();
    println("ok=" + cast<string>(b.count));
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
ExpectedStdout: EQUALS `ok=1
`
ExpectedStderr: DISCARD
