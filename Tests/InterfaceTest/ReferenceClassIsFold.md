# ReferenceClassIsFold
## Description
Static `is I` on a reference class folds the POSITIVE side only (vtable hit → compile-time `true`); the negative side stays a runtime ISINSTANCE check (a subclass may implement MORE interfaces than the static type shows — same conservatism as the class-is rule). The runtime path must still answer correctly (false for an unimplemented interface).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IVal { fun v(this) -> i64; }
interface IOther {}

class R {
    s: string;
    fun new(mut this, s: string) { this.s = s; }
    impl IReferenceType;
    impl IVal { fun v(this) -> i64 { return cast<i64>(this.s.length()); } }
}

initial {
    let obj: R = new R("abc");
    println(cast<string>(obj is IVal));
    println(cast<string>(obj is IOther));
    println(cast<string>(obj.v()));
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
ExpectedStdout: EQUALS `true
false
3
`
ExpectedStderr: DISCARD
