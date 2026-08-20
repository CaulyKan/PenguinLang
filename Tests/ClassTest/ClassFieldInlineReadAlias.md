# ClassFieldInlineReadAlias
## Description
Value-copy semantics: reading an inline value-class CLASS FIELD (`let b = w.p`
where p: Point, Point is a value class) COPIES the field storage on both
BabyPenguin and EmperorPenguin (fresh alloca + memcpy / runtime copy). Later
mutation through a separate mut extraction (`let q: mut Point = w.p; q.x = 9`)
is NOT visible to the earlier binding — prints 0. Chain writes through the
slot (`w.p.x = 9`) remain lvalue-addressed and stick (see the write-chain
pre-scan); only BINDING extraction copies.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; } }
class Wrap { p: Point = new Point(0, 0); }

initial {
    let mut w = new Wrap();
    let b = w.p;
    let q: mut Point = w.p;
    q.x = 9;
    print(cast<string>(b.x));
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
ExpectedStdout: EQUALS `0`
ExpectedStderr: DISCARD
