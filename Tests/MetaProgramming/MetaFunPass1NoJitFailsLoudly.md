# MetaFunPass1NoJitFailsLoudly
## Description
Bug #8 sentinel (batch 5 handover, FIXED in bugfix round 2): ANY CALLED `#fun` on the pass1 backend (EmperorPenguin running on the BabyPenguin VM) must fail the compile LOUDLY with a clean error — it can never evaluate, because the C# VM has no meta JIT (`penguin_jit_*` exist there only as unimplemented extern declarations; meta `#fun` evaluation is native Pass2/Pass3 only by design). History: when filed, the same shape crashed deep inside the VM with `E_RUNTIME_INVALID_OP ... left=NotInitializedRuntimeValue:!mut void ... func=emperor.MetaEngine.init` — root cause (fixed with this): a called extern with no C# implementation silently returned void (the extern's empty-body IR stub returned null, leaving the call's result register never stored), and the void propagated into `this.jit_ctx == 0`'s comparison; the same hole silently mis-evaluated any program calling a native-only extern (e.g. `dv=0` instead of `dv=7` on the cs backend — an unrepresentable program compiling clean). The VM now marks extern stubs (IRGenerator sets IRFunction.IsExtern from the semantic FunctionSymbol) and every CALL path — direct (2 switches) and function-pointer (2 variants) — throws `extern function 'X' is declared but has no implementation in the BabyPenguin VM (externs bind to native code in EmperorPenguin Pass2/Pass3 only)` with exit 1. This test asserts exactly that: pass1 compile fails nonzero with the clean stderr message. The pass2/pass3 positive half (`dv=7`) is covered by the MetaFun* family (e.g. MetaFunCliDefineVisible).
## Apply To
* EmperorPenguin Pass1

## Test Code
```
#fun default_value() -> i64 {
    return 7;
}
initial {
    println("dv=" + cast<string>(#default_value()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: MATCH `error[E_RUNTIME_LOOKUP]: extern function '__builtin_penguin_jit_create' is declared but has no implementation in the BabyPenguin VM.*`
