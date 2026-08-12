# MetaJsonContainers
## Description
Auto-impl over container fields: a class `ItemBox` with `std.Vector<i64>` (serializes to a JSON array, deserializes via `as_array()` + push), a nested `IJsonSerializable` class field `Child` (serialized via `value_raw`, deserialized via `Child.json_deserialize`), and a `std.HashMap<string,string>` (serialized via `key_iter()` as a JSON object, deserialized via `as_object()` + put). Container fields are field-initialized (`= new Vector/HashMap()`) so deserialize can push into them. Single-entry HashMap keeps the serialized object's key order deterministic. Full round-trip asserted. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Child {
    v: i64;
    #impl_json_serializable();
}
class ItemBox {
    name: string;
    count: i64;
    items: mut std.Vector<i64> = new std.Vector<i64>();
    tags: mut std.HashMap<string, string> = new std.HashMap<string, string>();
    child: Child;
    #impl_json_serializable();
}
initial {
    let b: mut ItemBox = new ItemBox();
    b.name = "box";
    b.count = 5;
    b.items.push(10);
    b.items.push(20);
    let ch: mut Child = new Child();
    ch.v = 7;
    b.child = ch;
    b.tags.put("k1", "v1");
    println("json=" + b.json_serialize());

    let b2: mut ItemBox = ItemBox.json_deserialize(b.json_serialize());
    println("name=" + b2.name + " count=" + cast<string>(b2.count));
    println("items0=" + cast<string>(b2.items.at(0).some) + " items1=" + cast<string>(b2.items.at(1).some));
    println("child_v=" + cast<string>(b2.child.v));
    println("tag_k1=" + b2.tags.get("k1").some);
    println("round=" + b2.json_serialize());
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
ExpectedStdout: EQUALS `json={"name":"box","count":5,"items":[10,20],"tags":{"k1":"v1"},"child":{"v":7}}
name=box count=5
items0=10 items1=20
child_v=7
tag_k1=v1
round={"name":"box","count":5,"items":[10,20],"tags":{"k1":"v1"},"child":{"v":7}}
`
ExpectedStderr: DISCARD
