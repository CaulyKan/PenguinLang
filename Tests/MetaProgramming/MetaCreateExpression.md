# MetaCreateExpression
## Description
Phase 5c: `#create_expression("code")` parses a literal code fragment into a structured AST node at unit-B compile time and registers it under a token; a `#fun -> ast` returns that token, and the host re-binds the AST node at the call site. Demonstrates BOTH splice positions: `#make_sum()` returns `10 + 5` spliced in expression position (`let x = #make_sum()` → 15), and `#emit_line()` returns `println("generated")` spliced in statement position. (Escape handling resolves the inner `\"` so the fragment reaches the Lexer as `println("generated")`.) Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun make_sum() -> ast {
    return #create_expression("10 + 5");
}
#fun emit_line() -> ast {
    return #create_expression("println(\"generated\")");
}
initial {
    let x: i64 = #make_sum();
    println("sum=" + cast<string>(x));
    #emit_line();
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
ExpectedStdout: EQUALS `sum=15
generated
`
ExpectedStderr: DISCARD
