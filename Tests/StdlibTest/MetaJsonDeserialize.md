# MetaJsonDeserialize
## Description
Auto-impl deserialize path: `#impl_json_serializable();` inside `class Point` generates `json_deserialize(json) -> mut Point` — parses via `std.parse_json`, then assigns each field from `_v.get("<name>").some` (try-bind `:=`), with `cast` for narrow integers. The static `Point.json_deserialize(...)` call (interface-impl method with no `this`) works like `Foo.foo()` in InterfaceStaticFunctionTest. Round-trip: serialize -> deserialize -> reserialize is byte-identical. Requires native Pass2/Pass3.

**RED SENTINEL (known regression on feature/value-enum-size, not on master)**:
the generated `json_deserialize` crashes at runtime (exit 139) — same
derive-pipeline corruption family as MetaJsonCustomImpl (wrong field
assignment through the generated code; at 544e4bb it was already broken
with E_RESOLVE_TYPE 'std.IJsonSerializable<...>'). Green on master
(verified 2026-08-19 via a master worktree bootstrap). NOTE when
re-verifying manually: the std json/hashmap/vector Compile.Args must
PRECEDE the source file. Should turn green once the derive-pipeline
regression (544e4bb..2c01777 era) is fixed. No known-good compiler applies
(meta-only syntax).

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
    let p: mut Point = Point.json_deserialize("{\"x\":10,\"y\":20,\"label\":\"pt\"}");
    println("x=" + cast<string>(p.x) + " y=" + cast<string>(p.y) + " label=" + p.label);
    println("round=" + p.json_serialize());
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
ExpectedStdout: EQUALS `x=10 y=20 label=pt
round={"x":10,"y":20,"label":"pt"}
`
ExpectedStderr: DISCARD
