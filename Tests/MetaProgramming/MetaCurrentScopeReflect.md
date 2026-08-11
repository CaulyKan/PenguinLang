# MetaCurrentScopeReflect
## Description
The full json.penguin reflection pattern: `get_current_scope()` + `t.fields()` (AST-fallback reflection at 5a splice time) + computed `compiler().create_definition`. `#gen_field_count()` inside `class Point` reads its own scope, counts fields via `t.fields().size()` (= 2 for `x; y`), and injects a class method returning the count. Asserts the field COUNT (the AST fallback populates names/count but not field types at splice time — see `resolve_type_from_ast`). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32; y: i32; #gen_field_count(); }
#fun gen_field_count() -> ast {
    let t: emperor.BoundType = compiler().get_current_scope();
    let n = t.fields().size();
    return compiler().create_definition(
        "fun field_count(this) -> i64 { return " + cast<string>(n) + "; }");
}
initial {
    let p: mut Point = new Point();
    println("fc=" + cast<string>(p.field_count()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `fc=2
`
ExpectedStderr: DISCARD
