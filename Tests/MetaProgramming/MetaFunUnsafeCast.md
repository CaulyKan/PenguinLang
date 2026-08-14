# MetaFunUnsafeCast
## Description
`unsafe_cast<T>(expr)` — raw i64↔reference/pointer conversion with NO runtime type check (inttoptr/ptrtoint), the foundational brick of the M6-step2 object ABI (`penguin_meta_get_object` / `as<T>()` both need it). `cast<T>` stays checked. This test round-trips a reference through i64 and back: a `new C()` (x=0) → `unsafe_cast<i64>` (ptrtoint) → `unsafe_cast<C>` (inttoptr) recovers the same object, so `c2.x` is still 0. Verified on native Pass2/Pass3 (LLVM ptrtoint/inttoptr emission).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class C {
    x: i64 = 0;
}
initial {
    let c = new C();
    let p: i64 = unsafe_cast<i64>(c);
    let c2: C = unsafe_cast<C>(p);
    println("x=" + cast<string>(c2.x));
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
ExpectedStdout: EQUALS `x=0
`
ExpectedStderr: DISCARD
