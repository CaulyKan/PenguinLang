# MetaCurrentScope
## Description
`compiler().get_current_scope()` — a class-member `#fun` knows its enclosing class: `#who_am_i()` spliced inside `class Point` reads the live `BoundType` of `Point` (via `current_scope_class_name`, set during the 5a class-member meta rewrite) and generates a class method returning `Point`. The generated method is called on a `Point` instance from `initial`. This is the json.penguin auto-impl pattern's first building block. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32; #who_am_i(); }
#fun who_am_i() -> ast {
    let t: emperor.BoundType = compiler().get_current_scope();
    return compiler().create_definition(
        "fun get_cls_name(this) -> string { return \"" + t.display_name() + "\"; }");
}
initial {
    let p: mut Point = new Point();
    println("cls=" + p.get_cls_name());
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
ExpectedStdout: EQUALS `cls=Point
`
ExpectedStderr: DISCARD
