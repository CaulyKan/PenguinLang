# ClassFieldChainWriteThrough
## Description
Value-copy semantics, chain-write half: a write through an inline value-class
class field (`w.p.x = 42`) is LVALUE-ADDRESSED — it writes directly into the
field slot inside w (the write-chain pre-scan aliases the RDMBR to the field
slot; no copy intervenes), so the write sticks and prints 42. This is the
complement of ClassFieldInlineReadAlias: BINDING extraction (`let b = w.p`)
copies, but chain writes reach the real slot.

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
    w.p.x = 42;
    print(cast<string>(w.p.x));
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
ExpectedStdout: EQUALS `42`
ExpectedStderr: DISCARD
