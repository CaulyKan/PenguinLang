# TryBindEnumElseInvisible
## Description
Negative: the try-bind variable is visible ONLY in the then-branch (its scope is the then-block), so referencing it in the `else` branch must be a compile error (`E_RESOLVE_SYMBOL`). This is asserted on EmperorPenguin (strict BoundScope). BabyPenguin is intentionally EXCLUDED: its scope model is lenient for all block-scoped locals (single-candidate symbols resolve across sibling scopes), so it accepts this — a pre-existing language-wide behavior, not a regression.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    enum OptVal { some: i32; none; }
    initial {
        let z = new OptVal.some(5);
        if (let a := z.some) {
            println("ok");
        } else {
            println(cast<string>(a));
        }
    }
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
