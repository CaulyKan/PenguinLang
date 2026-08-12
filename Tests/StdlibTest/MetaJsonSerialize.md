# MetaJsonSerialize
## Description
Meta-driven auto-impl: `#impl_json_serializable();` inside `class Point` expands (at 5a splice time) to an `impl std.IJsonSerializable<Point>` block. The `#fun impl_json_serializable` reads its enclosing class via `compiler().get_current_scope()`, reflects `t.fields()` (AST-fallback reflection — field names AND types, now enriched), and emits a `json_serialize` that streams each field through `std.JsonWriter` in declaration order. `p.json_serialize()` output is byte-exact. Requires native Pass2/Pass3 (meta JIT + reflection).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Point {
    x: i32;
    y: i32;
    label: string;
    #impl_json_serializable();
}
initial {
    let p: mut Point = new Point();
    p.x = 3;
    p.y = 4;
    p.label = "hi";
    println("json=" + p.json_serialize());
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
ExpectedStdout: EQUALS `json={"x":3,"y":4,"label":"hi"}
`
ExpectedStderr: DISCARD
