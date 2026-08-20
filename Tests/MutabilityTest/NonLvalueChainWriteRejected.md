# NonLvalueChainWriteRejected
## Description
Non-lvalue chain write rejection: the innermost base of a member-write chain
must be addressable storage. Writing through a temporary (`makeWrap().p.x = 9`,
`l.at(0).x = 9`, `cast<W>(x).p.x = 9`) would silently mutate a discarded copy
under value-copy semantics — the binder must reject it at compile time with
error[E_MUTABILITY] instead of emitting a write that can never be observed.
Mirrors the BabyPenguin ICodeContainer check.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; } }
class Wrap { p: Point = new Point(0, 0); }

fun makeWrap() -> Wrap { return new Wrap(); }

initial {
    makeWrap().p.x = 9;
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
