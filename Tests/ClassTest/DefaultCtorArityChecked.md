# ClassDefaultConstructorArityChecked

## Description
Calling the auto-generated default constructor with field arguments is a compile error: without an explicit `fun new(mut this, ...)`, the default constructor takes no arguments. FIXED: `bind_new_expr` in `EmperorPenguin/src/bound/SemanticBindExpressions.penguin` now checks the class constructor's parameter count (template or specialized def — the count is identical) and reports `E_CALL_ARITY`, matching BabyPenguin's behavior. Previously Pass2/Pass3 accepted the call silently and constructed the object with zeroed fields.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Point {
        x : i32;
        y : i32;
    }

    initial {
        let p : mut Point = new Point(3, 4);
        println(cast<string>(p.x));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
