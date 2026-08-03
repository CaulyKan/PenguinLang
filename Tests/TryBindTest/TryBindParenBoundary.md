# TryBindParenBoundary
## Description
Negative (P2): a pattern variable bound inside a parenthesized try-bind is scoped to the parens — referencing it after the parens is a compile error (`E_RESOLVE_SYMBOL`). (BabyPenguin does not yet support the expression form — Apply To is EmperorPenguin only.)

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __p2 {
    enum OptVal { some: i32; none; }
    fun paren_leak(o: OptVal) -> string {
        let r = (let x := o.some);
        return cast<string>(x);
    }
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
