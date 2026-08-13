# MetaJsonOptionBox
## Description
The `#specializing __builtin.Option<T>` / `#specializing __builtin.Box<T>` blocks in json.penguin inject an `impl std.IJsonSerializable<Option<T>>` / `impl std.IJsonSerializable<Box<T>>` when the element `T` has a real `IJsonSerializable` impl. `Option<i64>` serializes to the wrapped value for `some` and `"null"` for `none`; `Box<i64>` serializes to its `value`. Instance method dispatch `o.json_serialize()` / `b.json_serialize()` must lower to the injected impl (not the type symbol). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let o: __builtin.Option<i64> = new __builtin.Option<i64>.some(42);
    println("opt=" + o.json_serialize());
    let n: __builtin.Option<i64> = new __builtin.Option<i64>.none();
    println("none=" + n.json_serialize());
    let b: __builtin.Box<i64> = new __builtin.Box<i64>(7);
    println("box=" + b.json_serialize());
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
ExpectedStdout: EQUALS `opt=42
none=null
box=7
`
ExpectedStderr: DISCARD
