# MetaCreateExpressionLambda
## Description
`compiler().create_expression` producing a LAMBDA fragment: the parsed LambdaFunctionExpression AST node is returned as an `ast` token and spliced in expression position at the use site, where it re-binds through the normal pipeline — closure class synthesis happens in the RUNTIME unit (unit A), and the resulting fun value is called indirectly at runtime. This locks that spliced lambdas capture-analyze and bind at the use site, not in the meta unit.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun mk_fun_ast() -> ast {
    return compiler().create_expression("fun (x: i64) -> i64 { return x * 2; }");
}
initial {
    let g: fun<i64, i64> = #mk_fun_ast();
    println(cast<string>(g(21)));
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
