# GenericNewAndMethodInLoop
## Description
FIXED (was a red sentinel on Pass2/Pass3). A generic class instantiated inside a loop body AND used in a method call (`w.setv(5)`) failed with `use of undefined value '%t3'`: `collect_from_ast_expr_safe` had no `if_expr`/`while_expr` branch, and `parse_codeBlockExpression` wraps if/while into `Statement.block_expr` holding if/while *expressions* — so `new W<i64>()` inside a while body was never collected by the AST-driven monomorphizer, the class was never specialized, `emit_new` found no layout (`; NEW W__i64 (no layout)`) and emitted no allocation, and the method call used an undefined register. Root cause: `SemanticModel.collect_from_ast_expr_safe` (and the same gap in `collect_func_inst_from_expr`'s code-block handling). Fixed by recursing into `if_expr`/`while_expr` (condition/body), binary/unary/parenthesized sub-expressions, and code-block `trailing_expr`, plus a statement-level recursion helper `collect_func_inst_from_expr_stmt`. Now `W__i64` is specialized and the program prints `ok`.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class W {
    v: mut i64;
    fun new(mut this, a: i64) { this.v = a; }
    fun setv(mut this, a: i64) { this.v = a; }
}
initial {
    let i: mut i64 = 0;
    while (i < 1) {
        let w = new W<i64>(0);
        w.setv(5);
        i = i + 1;
    }
    println("ok");
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
ExpectedStdout: EQUALS `ok
`
ExpectedStderr: DISCARD
