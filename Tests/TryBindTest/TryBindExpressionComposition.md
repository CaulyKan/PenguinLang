# TryBindExpressionComposition
## Description
P2 general expression form: `let a [: T] := b` used as a boolean VALUE composed with `&&` (`let r = (let x := o.some && x > 10)`), and inside an if-condition `&&` chain where the pattern variable is visible in the then-branch (`if ((let a := o.some) && a > 10)`). Also `||` composition. (The negative cases — `||` RHS isolation, paren boundary — are covered by separate EmperorPenguin-only tests because BabyPenguin's scope model is lenient.)

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __p2 {
    enum OptVal { some: i32; none; }
    fun check_val(o: OptVal) -> string {
        // try-bind as a boolean value composed with &&
        let r = (let x := o.some && x > 10);
        if (r) { return "big"; }
        return "small";
    }
    fun check_cond(o: OptVal) -> string {
        // try-bind in an if-condition && chain; `a` visible in then-branch
        if ((let a := o.some) && a > 10) {
            return "cond-big:" + cast<string>(a);
        }
        return "cond-small";
    }
    fun check_or(o: OptVal, def: OptVal) -> string {
        // try-bind as a boolean value in ||
        let r = ((let b := o.some) || (def is OptVal.some));
        if (r) { return "or-true"; }
        return "or-false";
    }
    initial {
        println(check_val(new OptVal.some(42)));
        println(check_val(new OptVal.some(5)));
        println(check_val(new OptVal.none()));
        println(check_cond(new OptVal.some(42)));
        println(check_cond(new OptVal.some(5)));
        println(check_or(new OptVal.some(1), new OptVal.none()));
        println(check_or(new OptVal.none(), new OptVal.some(0)));
    }
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
ExpectedStdout: EQUALS `big
small
small
cond-big:42
cond-small
or-true
or-true
`
ExpectedStderr: DISCARD
