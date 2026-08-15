# BareVariantConstructionError
## Description
Negative test: constructing an enum variant WITHOUT `new` — `Option<i64>.some(42)` — must fail compilation with a clean `E_CALL_NOT_FUNCTION` diagnostic. EmperorPenguin previously bound this silently to a void-typed call with no callee symbol, poisoning dependent expressions and dying in the IR generator with `E_INTERNAL: Function call has no callee symbol`; bind_function_call now reports `'Option<i64>.some' is not a valid function (enum variants are constructed with 'new')` (first-use: BabyPenguin already diagnosed this; EmperorPenguin catches up). The follow-up `x.get_unique_name()` stays a dependent void expression — exactly one error, no internal crash. Correct construction (`new Option<i64>.some(42)`) is covered by MetaInjectedImplDirectCall.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let x = Option<i64>.some(42);
    println(x.get_unique_name());
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
