# MetaAstIntrospect
## Description
R2 ast introspection: a `#fun` receives an `ast` arg (i64 token), calls `emperor.penguin_meta_get_ast_expression(token)` to get the **real** `emperor.Expression`, and pattern-matches its variant. `42` → `Expression.constant` → returns 1; `1 + 2` → `Expression.binary` → returns 2. Proves the opt-in introspection bridge works end-to-end (the #fun can read AST structure). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun ast_kind(a: ast) -> i64 {
    let expr: emperor.Expression = emperor.penguin_meta_get_ast_expression(a);
    if (expr is emperor.Expression.constant) {
        return 1;
    }
    if (expr is emperor.Expression.binary) {
        return 2;
    }
    return 0;
}
initial {
    let k1: i64 = #ast_kind(42);
    let k2: i64 = #ast_kind(1 + 2);
    println("k1=" + cast<string>(k1) + " k2=" + cast<string>(k2));
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
ExpectedStdout: EQUALS `k1=1 k2=2
`
ExpectedStderr: DISCARD
