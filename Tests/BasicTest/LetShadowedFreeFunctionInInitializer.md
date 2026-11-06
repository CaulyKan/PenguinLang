# LetShadowedFreeFunctionInInitializer
## Description
A `let` whose declared local shadows a namespace-level free function, with the
initializer calling that same name (`let f: string = f("...")`). The correct
behavior (verified on EmperorPenguin Pass2/Pass3, and now BabyPenguin too): the
initializer resolves the name to the FUNCTION (the local is not in scope until
its declaration completes), the program compiles and prints the argument.

FIXED on BabyPenguin (O6-E, 2026-11-05): SemanticModel.ResolveShortSymbol gained
an excludeSymbols parameter (null = zero cost) and ICodeContainer opens a
name-resolution exclusion window while compiling a `let` declaration's
initializer (CodeContainerStorage.InitializingVariables, pushed around
InferVariableType and AddExpression) — the three identifier-resolution sites
(the ResolveExpressionType identifier arm, the AddPrimaryExpression identifier
arm, and the simple-call callee lookup) pass the window snapshot, so the
declared name resolves to outer symbols inside its own initializer, exactly
like EmperorPenguin's binding order. Previously BabyPenguin crashed with an
unhandled `InvalidOperationException: Sequence contains no elements` from
`ICodeContainer.ResolveExpressionType` (`funcType.GenericArguments.First()` on
a non-func type — the callee resolved to the just-declared `string` local).
A genuinely self-referential declaration with no outer symbol
(`let x: i32 = x;`) now reports a clean E_RESOLVE_SYMBOL.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun cast_op(a: string) -> string { return a; }
initial {
    let cast_op: string = cast_op("shadow-ok");
    println(cast_op);
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
ExpectedStdout: EQUALS `shadow-ok
`
ExpectedStderr: DISCARD
