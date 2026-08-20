# EnumMixedPayloadRefVariantWriteThrough
## Description
A MIXED enum (one value-class variant inlined in the union slot + one
reference-class variant storing a pointer in the same slot) must decide
inline-vs-pointer storage PER VARIANT when a chained write goes through a
payload extraction (`e.r.field = x`). The mut-ref RDENUM path used the
union's llvm type (`%class.<largest inline variant>`) to pick slot-aliasing,
so the reference variant aliased the union slack: the write landed past the
stored object pointer and was silently lost (the for-in desugar's
`ast_iter_call.function_call.callee = ...` died this way — callee stayed
none and IR lowering aborted with E_INTERNAL "Function call has no callee
symbol"). Reference variant must dereference the slot pointer; value variant
keeps the inline alias. Verified on all four compilers.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class RefFoo {
        impl __builtin.IReferenceType;
        cal: mut Option<i64> = new Option<i64>.none();
    }
    class ValFoo {
        cal: mut Option<i64> = new Option<i64>.none();
    }
    enum Mixed {
        v: ValFoo;
        r: RefFoo;
    }
    initial {
        let e: Mixed = new Mixed.r(new RefFoo());
        e.r.cal = new Option<i64>.some(3);
        let ev: Mixed = new Mixed.v(new ValFoo());
        ev.v.cal = new Option<i64>.some(7);
        println(e.r.cal.value_or(0));
        println(ev.v.cal.value_or(0));
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
ExpectedStdout: EQUALS `3
7
`
ExpectedStderr: DISCARD
