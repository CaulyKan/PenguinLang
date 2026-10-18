# ValueClassIsFold
## Description
Static `is I` folding on a sealed value class folds BOTH ways: a positive hit from the vtable set folds to `true`, a miss folds to `false` (a value class cannot gain impls from a subclass — there is no inheritance). All-value-fields class `V` is classified as a value type on both sides.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IVal { fun v(this) -> i64; }
interface IOther {}

class V {
    x: i64;
    fun new(mut this, x: i64) { this.x = x; }
    impl IVal { fun v(this) -> i64 { return this.x; } }
}

initial {
    let obj: V = new V(7);
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
7
`
ExpectedStderr: DISCARD
