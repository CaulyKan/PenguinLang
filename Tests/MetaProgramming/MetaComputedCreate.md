# MetaComputedCreate
## Description
Phase 6 v2: a `#fun` reflects a type's field count via the real `t.fields().size()`, builds the expression string `"0 + 1 + 1"` at compile time (one `+ 1` per field), and `#create_expression` parses that COMPUTED string into an AST spliced at the call site — evaluating to `2` for the 2-field `Point`. Exercises real-reuse reflection (`t.fields()` direct method call) + computed-string codegen + plain `while` at meta-runtime. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point {
    x: i32;
    y: i32;
}
#fun count_expr(t: type) -> ast {
    let mut s = "0";
    let mut i = 0;
    let n: i64 = cast<i64>(t.fields().size());
    while (i < n) {
        s = s + " + 1";
        i = i + 1;
    }
    return #create_expression(s);
}
initial {
    println(cast<string>(#count_expr(#typeof(Point))));
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
ExpectedStdout: EQUALS `2
`
ExpectedStderr: DISCARD
