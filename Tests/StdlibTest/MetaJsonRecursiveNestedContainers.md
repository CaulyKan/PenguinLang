# MetaJsonRecursiveNestedContainers
## Description
`#impl_json_serializable()` over the two container shapes the LSP protocol layer needs, both regressed on 2026-08-31 and locked here: (1) a SELF-RECURSIVE container field — `kids: mut std.Vector<Node>` inside Node itself. At def-splice time the class's own impl member is not spliced into the AST yet, so the AST-fallback `has_interface("IJsonSerializable")` was false and the field was silently SKIPPED from both generated methods (fixed by comparing against `compiler().get_current_scope()` — the class whose #impl_json_serializable() is expanding). (2) A NESTED container as a HashMap value — `changes: mut std.HashMap<string, std.Vector<Edit>>`. The display-name SUBSTRING dispatch (`string_find(dn, "std.Vector")`) misrouted the HashMap to the Vector branch (its value-arg spelling contains "std.Vector"), and the read path had no container-value branch at all (`Vector.json_deserialize` does not exist). Fixed by base-name dispatch (segment before the first '<') plus recursive json_read_fill_stmt/json_read_push_stmt with depth-suffixed temps. Asserts full serialize → deserialize → re-serialize round trips for both shapes (three-level nesting for the recursive one). Requires native Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Node {
    name: mut string = "";
    kids: mut std.Vector<Node> = new std.Vector<Node>();
    #impl_json_serializable();
    fun new(mut this) {}
}
class Edit {
    newText: mut string = "";
    #impl_json_serializable();
    fun new(mut this) {}
}
class WEdit {
    changes: mut std.HashMap<string, std.Vector<Edit>> = new std.HashMap<string, std.Vector<Edit>>();
    #impl_json_serializable();
    fun new(mut this) {}
}
initial {
    let n: mut Node = new Node();
    n.name = "root";
    let c: mut Node = new Node();
    c.name = "kid";
    let gc: mut Node = new Node();
    gc.name = "gkid";
    c.kids.push(gc);
    n.kids.push(c);
    println(n.json_serialize());
    let n2: mut Node = Node.json_deserialize(n.json_serialize());
    println("name=" + n2.name + " kids0=" + n2.kids.at(0).some.name + " gkid=" + n2.kids.at(0).some.kids.at(0).some.name);
    println("round=" + n2.json_serialize());

    let e: mut Edit = new Edit();
    e.newText = "x";
    let v: mut std.Vector<Edit> = new std.Vector<Edit>();
    v.push(e);
    let w: mut WEdit = new WEdit();
    w.changes.put("file:///a", v);
    println(w.json_serialize());
    let w2: mut WEdit = WEdit.json_deserialize(w.json_serialize());
    println("edit0=" + w2.changes.get("file:///a").some.at(0).some.newText);
    println("wround=" + w2.json_serialize());
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
ExpectedStdout: EQUALS `{"name":"root","kids":[{"name":"kid","kids":[{"name":"gkid","kids":[]}]}]}
name=root kids0=kid gkid=gkid
round={"name":"root","kids":[{"name":"kid","kids":[{"name":"gkid","kids":[]}]}]}
{"changes":{"file:///a":[{"newText":"x"}]}}
edit0=x
wround={"changes":{"file:///a":[{"newText":"x"}]}}
`
ExpectedStderr: DISCARD
