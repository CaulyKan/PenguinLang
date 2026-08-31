# MetaJsonVectorOfSerializable
## Description
Regression lock (was a RED sentinel, green since the json.penguin fix on 2026-08-31). `#impl_json_serializable()` on a class holding a CONTAINER of a user class (`kids: mut std.Vector<Kid>`, Kid itself auto-impl'd) failed to compile with `error[E_RESOLVE_SYMBOL]: Cannot resolve symbol 'Kid'` ×2. Root cause (NOT the interface-registration ordering the original sentinel suspected — a hand-expanded repro failed identically, exonerating the splice path): `Vector<Kid>` specialization cascades (`at() -> Option<T>`) instantiates `Option<Kid>`, whose `#specializing __builtin.Option<T>` block then INJECTS an `impl std.IJsonSerializable<Option<Kid>>`. That injected impl binds in the SPECIALIZED type's scope (std/builtin namespaces), which cannot see Kid's per-file `_ns_` namespace — and the spliced `#json_read_expr_ast(T, ...)` referenced the element by its CONCRETE short name (`Kid.json_deserialize(...)`), unresolvable there. Fix: json.penguin's `#json_read_expr_ast` takes the element SPELLING and the #specializing blocks pass the template param (`"T"`), which the specialized scope binds to the concrete type (per inject_specializing_impl's documented contract). Same-file splices keep the concrete spelling. Verified on Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Kid {
    v: mut i64 = 0;
    #impl_json_serializable();
    fun new(mut this) {}
}
class HolderVec {
    kids: mut std.Vector<Kid> = new std.Vector<Kid>();
    #impl_json_serializable();
    fun new(mut this) {}
}
initial {
    let k: mut Kid = new Kid();
    k.v = 9;
    let h: mut HolderVec = new HolderVec();
    h.kids.push(k);
    println(h.json_serialize());
}
```

## Compile
Args: `EmperorPenguin/std/penguin/json.penguin EmperorPenguin/std/penguin/hashmap.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `{"kids":[{"v":9}]}
`
ExpectedStderr: DISCARD
