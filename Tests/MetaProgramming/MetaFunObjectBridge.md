# MetaFunObjectBridge
## Description
`penguin_meta_register_object(obj)` / `penguin_meta_get_object(handle)` — the object bridge over HANDLE tokens (replaces the old raw-address bridge that needed unsafe_cast): register_object stores the live reference into the MetaEngine's handle registry (a GC-traced List<IReferenceType>, so the object stays alive precisely — no pin) and returns a 1-based handle; get_object resolves the handle back to the SAME object. This tests the full compile-time object pipeline inside a #fun: allocate a StringBuilder in the meta JIT's GC heap → `emperor.penguin_meta_register_object(sb)` takes a handle → `emperor.penguin_meta_get_object` recovers it as an IReferenceType → checked `cast<mut StringBuilder>` down → mutate + read the SAME object ("bridge" + "!" = "bridge!"). Verified on native Pass2/Pass3 (JIT-only feature).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun obj_bridge() -> string {
    let mut sb = new StringBuilder();
    sb.append("bridge");
    let h: i64 = emperor.penguin_meta_register_object(sb);
    let r = emperor.penguin_meta_get_object(h);
    let mut sb2 = cast<mut StringBuilder>(r);
    sb2.append("!");
    return sb2.to_string();
}
initial {
    println(#obj_bridge());
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
ExpectedStdout: EQUALS `bridge!
`
ExpectedStderr: DISCARD
