# MetaFunObjectBridge
## Description
`penguin_meta_get_object(ref)` — the M6-step2 object bridge that recovers a LIVE object from an object_ref address (pure inttoptr via unsafe_cast; the binder gates boxing on !is_unsafe so the raw i64 becomes a reference). This tests the full compile-time object-recovery pipeline inside a #fun: allocate a StringBuilder in the meta JIT's GC heap → `unsafe_cast<i64>` takes its address (object_ref) → `emperor.penguin_meta_get_object` recovers it as an IReferenceType → checked `cast<StringBuilder>` down → mutate + read the SAME object ("bridge" + "!" = "bridge!"). This is the machinery the object value-template-arg flow (later bricks) uses to introspect compile-time objects. Verified on native Pass2/Pass3 (JIT-only feature).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun obj_bridge() -> string {
    let mut sb = new StringBuilder();
    sb.append("bridge");
    let p: i64 = unsafe_cast<i64>(sb);
    let r = emperor.penguin_meta_get_object(p);
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
