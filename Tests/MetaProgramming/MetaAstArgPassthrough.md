# MetaAstArgPassthrough
## Description
Phase 5c: an `ast`-typed argument is captured as a structured AST node and registered under a token (distinct from `#create_expression`, which builds the node from a string); a `#fun -> ast` returns the token unchanged and the host re-binds the original node at the call site. `#echo_ast(6 * 7)` captures the `6 * 7` binary expression, returns it, and splices it back so `x` evaluates to 42. Exercises the full ast-arg register -> return -> rebind path. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun echo_ast(a: ast) -> ast {
    return a;
}
initial {
    let x: i64 = #echo_ast(6 * 7);
    println("x=" + cast<string>(x));
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
ExpectedStdout: EQUALS `x=42
`
ExpectedStderr: DISCARD
