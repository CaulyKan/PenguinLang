# MetaJsonCustomImpl
## Description
Req 7 + req 3: a class `Wrapped` implements `penguin.IJsonSerializable<Wrapped>` MANUALLY (no `#impl_json_serializable`), and `Holder` (via `#impl_json_serializable`) has a `w: Wrapped` field AND a `ns: NotSerializable` field whose class does NOT implement IJsonSerializable. The auto-impl reflects over Holder's fields, checks each class field type's implemented interfaces (`compiler().get_current_scope()` -> `t.fields()` -> `f.bound_type.class_def().has_interface("IJsonSerializable")` via the AST-fallback `implemented_interfaces`), serializes `w` via its impl (`value_raw(this.w.json_serialize())`) and SKIPS `ns` entirely (both directions). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Wrapped {
    n: i64;
    impl penguin.IJsonSerializable<Wrapped> {
        fun json_serialize(this) -> string {
            return "{\"wrapped\":" + cast<string>(this.n) + "}";
        }
        fun json_deserialize(json: string) -> mut Wrapped {
            let w: mut Wrapped = new Wrapped();
            w.n = 123;
            return w;
        }
    }
}
class NotSerializable {
    a: i64;
}
class Holder {
    name: string;
    w: Wrapped;
    ns: NotSerializable;
    #impl_json_serializable();
}
initial {
    let h: mut Holder = new Holder();
    h.name = "hold";
    let w: mut Wrapped = new Wrapped();
    w.n = 7;
    h.w = w;
    let ns: mut NotSerializable = new NotSerializable();
    ns.a = 1;
    h.ns = ns;
    println("json=" + h.json_serialize());
    let h2: mut Holder = Holder.json_deserialize(h.json_serialize());
    println("name=" + h2.name + " wn=" + cast<string>(h2.w.n));
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
ExpectedStdout: EQUALS `json={"name":"hold","w":{"wrapped":7}}
name=hold wn=123
`
ExpectedStderr: DISCARD
