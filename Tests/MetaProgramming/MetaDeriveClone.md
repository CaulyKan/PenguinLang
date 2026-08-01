# MetaDeriveClone
## Description
R3+R4: `#derive_clone(#typeof(Point))` at top-level — a `#fun` reads `t.fields()` (via the AST fallback, since def-splice runs before types are bound), iterates the field names, builds a clone function source string at compile time, and `#create_definition(computed_string)` generates + injects `fun my_clone(p: mut Point) -> Point { ... }`. The generated function is callable from `initial`. Exercises: AST fallback reflection + computed `#create_definition` + def-splice injection — the full derive- macro pipeline. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: mut i32; y: mut i32; }
#fun derive_clone(t: type) -> ast {
    let fs = t.fields();
    let n = cast<i64>(fs.size());
    let mut body = "fun my_clone(p: mut Point) -> Point { let q: mut Point = new Point(); ";
    let i: mut i64 = 0;
    while (i < n) {
        let fname = fs.at(cast<u64>(i)).some.name;
        body = body + "q." + fname + " = p." + fname + "; ";
        i = i + 1;
    }
    body = body + "return q; }";
    return #create_definition(body);
}
#derive_clone(#typeof(Point));
initial {
    let p: mut Point = new Point();
    p.x = 3; p.y = 4;
    let q: Point = my_clone(p);
    println(cast<string>(q.x) + "," + cast<string>(q.y));
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
ExpectedStdout: EQUALS `3,4
`
ExpectedStderr: DISCARD
