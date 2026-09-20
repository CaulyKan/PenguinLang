# LambdaCaptureChainedCall
## Description
An inline capturing lambda passed as a call argument loses its captured fields when the call's RESULT is immediately chained with another member call: `p.pick(fun (x: string) -> bool { return x == nm; }, "aa").size()` — the callee's parameter `nm` (any enclosing local/param) is captured by value.

**RED SENTINEL (known bug on all EmperorPenguin passes, green on BabyPenguin)**: binding the chained call synthesizes the closure class TWICE — the first bind runs the capture analysis and rewrites the lambda body IN PLACE (`nm` → `this.nm`, see `lam_visit_expr` in `SemanticBindExpressions.penguin` ~line 1409); the re-bind of the member-call base then synthesizes a second `__lambda_<n>` from the already-rewritten AST, finds no free identifiers left, and emits the closure with ZERO capture fields while the body still references them, failing with `error[E_RESOLVE_SYMBOL]: Type 'mut __lambda_2' has no member 'nm'` (and a follow-on return-type mismatch). Verified red on Pass1/Pass2/Pass3; BabyPenguin prints `1`. Workaround (used by Examples/simple_sql codegen): bind the lambda to a local `let pred: fun<...> = fun ...;` first and pass the variable. Should turn green once the double-bind / in-place-rewrite interaction is fixed.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Res {
    fun size(this) -> i64 { return 1; }
}
class Picker {
    fun pick(this, f: fun<bool, string>, s: string) -> mut Res {
        let r: mut Res = new Res();
        if (f(s)) { return r; }
        return r;
    }
}
fun go(nm: string) -> i64 {
    let p: mut Picker = new Picker();
    return p.pick(fun (x: string) -> bool { return x == nm; }, "aa").size();
}
initial { println(cast<string>(go("aa"))); }
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
ExpectedStdout: EQUALS `1
`
ExpectedStderr: DISCARD
