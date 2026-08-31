# PrimitiveToStringNotDefined
## Description
Negative-compile guard: primitives have no `to_string()` method in PenguinLang (`cast<string>(v)` is the conversion) — neither BabyPenguin nor EmperorPenguin stdlib defines one, so `v.to_string()` on an i64 must fail to compile. BabyPenguin reports a clean `error[E_RESOLVE_SYMBOL]: Cant resolve symbol 'to_string'`; EmperorPenguin currently reports `error[E_INTERNAL]: Function call has no callee symbol` (worse diagnostic, same reject outcome) — the exit code + empty stdout are what this test locks in, so a future change can never make the call silently compile to junk code.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let v: i64 = 42;
    println("age=" + v.to_string());
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: EQUALS ``
ExpectedStderr: DISCARD
