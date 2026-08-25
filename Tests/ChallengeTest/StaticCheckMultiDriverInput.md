# StaticCheckMultiDriverInput
## Description
Design (rtl-ports-design.md Q2/Q7): an input port has exactly one driver — connecting two sources to the same input is a compile-time error[E_WIRING] ("Input port ... is connected twice"). The check pools all construct functions of one scope, so cross-block duplicate connects on the same hoisted instance fire as well.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class M { input x : i64; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
construct { let a : mut i64 = 1; let b : mut i64 = 2; let m : mut M = new M(); connect(a, m.x); connect(b, m.x); }
initial { println("no-error"); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `connected twice`
