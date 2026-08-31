# MetaJsonOptionBoxFieldAutoImpl
## Description
`#impl_json_serializable()` on a class holding `Option<i64>` / `Box<i64>` fields — serialize AND deserialize round-trip. Regression lock (was broken): the generated deserialize for such fields reads `__builtin.Option<i64>.json_deserialize(_f.to_json())` (json_type_spelling emits the QUALIFIED source spelling), and the namespace branch of member-access binding DROPPED the member's generic args — the base bound to the bare `Option`/`Box` TEMPLATE type, whose scope/vtables carry no `#specializing`-injected impl (it lives on the specialized def `Option__i64`/`Box__i64` only), so the compile failed with `error[E_RESOLVE_SYMBOL]: Type 'Option' has no member 'json_deserialize'` (and the same for Box). Root cause in `SemanticBindExpressions.bind_member_access`; fixed by `bind_namespaced_generic_type_member`, which resolves the args and prefers the specialized def (same contract as bind_identifier's generic-type branch). The bare-name spelling `Option<i64>.json_deserialize(...)` already worked (MetaJsonGenericStaticCall). NOTE: like every `#impl_json_serializable()` class, Node needs a no-arg constructor (the generated deserialize builds `new Node()`). Verified on Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    name: string;
    age: __builtin.Option<i64>;
    tag: __builtin.Box<i64>;
    #impl_json_serializable();
    fun new(mut this) {}
}
initial {
    let n: mut Node = new Node();
    n.name = "bob";
    n.age = new __builtin.Option<i64>.some(3);
    n.tag = new __builtin.Box<i64>(9);
    let s: string = n.json_serialize();
    println(s);
    let m: mut Node = Node.json_deserialize(s);
    println(m.name + " " + cast<string>(m.age.value_or(-1)) + " " + cast<string>(m.tag.value));
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
ExpectedStdout: EQUALS `{"name":"bob","age":3,"tag":9}
bob 3 9
`
ExpectedStderr: DISCARD
