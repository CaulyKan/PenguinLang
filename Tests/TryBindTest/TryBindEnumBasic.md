# TryBindEnumBasic
## Description
Try-bind `if (let x := o.some)`: enum variant check + payload binding in a single expression. `x` is bound to the payload only when the variant matches. Covers the matched and non-matched (else) paths. Feature: `let a [: T] := b` try-bind expression (P1 enum variant form).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    enum OptVal { some: i32; none; }
    fun get(o: OptVal) -> string {
        if (let x := o.some) {
            return "some:" + cast<string>(x);
        } else {
            return "none";
        }
    }
    initial {
        println(get(new OptVal.some(42)));
        println(get(new OptVal.none()));
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
ExpectedStdout: EQUALS `some:42
none
`
ExpectedStderr: DISCARD
