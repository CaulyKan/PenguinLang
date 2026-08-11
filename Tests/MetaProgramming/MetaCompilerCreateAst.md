# MetaCompilerCreateAst
## Description
AST construction via the `#compiler()` facade:
- `create_ast("fun from_ast() -> i64 { return 11; }")` — parses a full definition (expr-parse fails first, falls back to a 1-def compilation unit); def-position splice injects `fun from_ast()`.
- `create_function_ast("gen_fun", resolve_type("i64").some, "return 22;")` — builds `fun gen_fun() -> i64 { return 22; }` from a name + return `BoundType` + body; def-position splice injects it.
- `create_empty_ast()` — returns an empty code-block token; expression-position splice is a no-op.

Both generated functions are callable from `initial`. Exercises the definition-splice (`#gen_a();` / `#gen_b();`) and expression-splice (`#gen_c();`) paths with `compiler()`-built ast. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun gen_a() -> ast { return compiler().create_ast("fun from_ast() -> i64 { return 11; }"); }
#fun gen_b() -> ast {
    return compiler().create_function_ast("gen_fun", compiler().resolve_type("i64").some, "return 22;");
}
#fun gen_c() -> ast { return compiler().create_empty_ast(); }

#gen_a();
#gen_b();
initial {
    #gen_c();
    println("a=" + cast<string>(from_ast()));
    println("b=" + cast<string>(gen_fun()));
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
ExpectedStdout: EQUALS `a=11
b=22
`
ExpectedStderr: DISCARD
