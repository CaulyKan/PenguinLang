# MetaFunObjectBridgeBadHandle
## Description
NEGATIVE: `penguin_meta_get_object(handle)` resolves handles ONLY through the MetaEngine's object registry — a handle that was never handed out (here 999) is rejected with a runtime error at compile time (the #fun runs during compilation), so compilation fails. This locks in the handle-bridge soundness property: unlike the retired raw-address bridge, an arbitrary i64 can never be turned into a reference. Companion of MetaFunObjectBridge. Pass2/Pass3 (JIT-only feature).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun bad_handle() -> i64 {
    let r = emperor.penguin_meta_get_object(999);
    return 0;
}
initial {
    println(#bad_handle());
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
