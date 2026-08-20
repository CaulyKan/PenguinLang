# MetaJsonCustomImpl
## Description
Req 7 + req 3: a class `Wrapped` implements `std.IJsonSerializable<Wrapped>` MANUALLY (no `#impl_json_serializable`), and `Holder` (via `#impl_json_serializable`) has a `w: Wrapped` field AND a `ns: NotSerializable` field whose class does NOT implement IJsonSerializable. The auto-impl reflects over Holder's fields, checks each class field type's implemented interfaces (`compiler().get_current_scope()` -> `t.fields()` -> `f.bound_type.class_def().has_interface("IJsonSerializable")` via the AST-fallback `implemented_interfaces`), serializes `w` via its impl (`value_raw(this.w.json_serialize())`) and SKIPS `ns` entirely (both directions). Requires native Pass2/Pass3.

**RED SENTINEL (known regression on feature/value-enum-size, not on master)**:
the generated `json_deserialize` assigns `Holder.w` a wrong object —
`h2.w.n` prints a run-varying address (the `name` string's data read as
i64) instead of 123; serialization itself is correct. At 544e4bb the first
symptom was E_RESOLVE_TYPE 'std.IJsonSerializable<Wrapped>' from the
generated impl (later branch commits fixed the resolve, leaving the
field-assignment corruption). Green on master (verified 2026-08-19 via a
master worktree bootstrap). NOTE when re-verifying manually: the std json/
hashmap/vector Compile.Args must PRECEDE the source file, or the interface
genuinely fails to resolve. Should turn green once the derive-pipeline
regression (544e4bb..2c01777 era) is fixed. No known-good compiler applies
(meta-only syntax).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Wrapped {
    n: i64;
    impl std.IJsonSerializable<Wrapped> {
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
