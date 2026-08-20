# MutBindingAliasValueClass
## Description
Value-copy semantics: `let mut b = a` COPIES the value-class struct into fresh
storage (mut is a compile-time permission and never changes storage identity).
Mutating `b.x` must not be visible through `a` — prints 1. Green on BabyPenguin
and all EmperorPenguin generations.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; } }

initial {
    let mut a = new Point(1, 2);
    let mut b = a;
    b.x = 9;
    print(cast<string>(a.x));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
