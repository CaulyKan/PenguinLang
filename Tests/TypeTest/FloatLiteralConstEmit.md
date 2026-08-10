# FloatLiteralConstEmit
## Description
Float literal assignment to `f32`/`f64` variables and `cast<string>` of floats work on all four compilers (BabyPenguin, EmperorPenguin Pass1/2/3). Formerly a RED SENTINEL: `LLVMEmitter.emit_const` had no float branch (emitted `add float 0, 3.5`, rejected by clang) and float→string cast fell through to `bitcast float to ptr`. Fixed by: float/double branches in `emit_const`/`emit_assign` (`fadd <type> 0.0, <val>` with literal normalization `3` → `3.0`), `f32`/`f64` aliases in `BasicTypeNodes.Nodes` (BabyPenguin) and `BoundTypeRegistry.resolve_type` (`float`/`double` aliases → f32/f64), and a float→string branch in `LLVMEmitter.emit_cast` backed by the new C runtime `_emperor_double_to_string` (`%g` formatting, matching C# `ToString()` for these values).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let f: f32 = 3.5;
    let g: f64 = 2.25;
    let h: f32 = 3;
    println(cast<string>(f) + " " + cast<string>(g));
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
ExpectedStdout: EQUALS `3.5 2.25
`
ExpectedStderr: DISCARD
