# EnumPayloadExtractSnapshot
## Description
Value-copy semantics (sentinel turned green): an immutable extraction of an
inline value-class enum payload (`let p = o.some`) COPIES the payload out of
the union slot — a snapshot. Mutation through a later mut extraction
(`let q: mut Point = o.some; q.x = 9`) is NOT visible to the earlier binding
— prints 1. Historically this was a red sentinel (BabyPenguin aliased the
payload, EmperorPenguin memcpy'd a snapshot); the value-copy semantics change
made both compilers copy, and the snapshot behavior became the contract.
Chain writes through the slot (`o.some.x = 9`) still address the slot
directly (write-chain pre-scan) — only BINDING extraction copies.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
enum Opt { some: Point; none; }

class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; } }

initial {
    let mut o = new Opt.some(new Point(1, 2));
    let p = o.some;
    let q: mut Point = o.some;
    q.x = 9;
    print(cast<string>(p.x));
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
