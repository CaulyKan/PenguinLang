# FloatLiteralUnaryNeg
## Description
A NEGATIVE float literal (`let neg: f32 = -3.5`) compiles and runs on all four compilers (BabyPenguin, EmperorPenguin Pass1/2/3). Formerly a RED SENTINEL: on pass2/pass3 the operand `3.5` inside unary neg `-3.5` was bound as an i64 literal (`bind_constant` hardcoded numeric literals to i64), so the IR emitted `add i64 0, 3.5` → clang `error: floating point constant invalid for type`. Fixed by detecting float literals (`.`, `e`/`E`) in `bind_constant` and binding them as f64, plus a numeric cast on the `let x: f32 = ...` binding.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let neg: f32 = -3.5;
    println("ok");
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
ExpectedStdout: EQUALS `ok
`
ExpectedStderr: DISCARD
