# TryBindOrRhsInvisible
## Description
Negative (P2): a pattern variable bound in one `||` operand must NOT be visible in a later `||` operand (short-circuit isolation) — referencing it is a compile error (`E_RESOLVE_SYMBOL`). This mirrors C# pattern-variable definite assignment. (BabyPenguin does not yet support the expression form — Apply To is EmperorPenguin only.)

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __p2 {
    enum OptVal { some: i32; none; }
    fun or_leak(o: OptVal) -> string {
        let r = ((let b := o.some) || b == 1);
        if (r) { return "t"; }
        return "f";
    }
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
