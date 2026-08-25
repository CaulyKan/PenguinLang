# StaticCheckUnconnectedInput
## Description
Design (Q3): an input port must be connected or carry a default initializer — otherwise compile error[E_WIRING] ("Input port ... is never connected"). The audit covers every module instantiated by a `let x = new C()` in a construct (per wiring pool); dynamic instantiation outside construct blocks keeps the graceful runtime park (see PortTest/UnconnectedInputWaits).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class M { input x : i64; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
construct { let m : mut M = new M(); }
initial { println("no-error"); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `is never connected`
