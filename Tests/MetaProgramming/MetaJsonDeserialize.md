# MetaJsonDeserialize
## Description
Auto-impl deserialize path: `#impl_json_serializable();` inside `class Point` generates `json_deserialize(json) -> mut Point` — parses via `penguin.parse_json`, then assigns each field from `_v.get("<name>").some` (try-bind `:=`), with `cast` for narrow integers. The static `Point.json_deserialize(...)` call (interface-impl method with no `this`) works like `Foo.foo()` in InterfaceStaticFunctionTest. Round-trip: serialize -> deserialize -> reserialize is byte-identical. Requires native Pass2/Pass3.

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
