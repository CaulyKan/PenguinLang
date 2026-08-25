# StaticCheckOutputDualDriver
## Description
Design (Q9): an output port has exactly one driver — module body code XOR a construct passthrough wire; the conflict is a compile error[E_WIRING] ("Output port ... has two drivers"). Writing the same output from two different routines is rejected the same way (one driving routine; merge streams through a channel). Previously one conflict shape silently let a single writer win while another stack-overflowed at runtime.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Inner { input x : i64 = 0; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
class Outer {
    input x : i64 = 0;
    output y : i64 = 0;
    construct { let inner : mut Inner = new Inner(); connect(this.x, inner.x); connect(inner.y, this.y); }
    initial { while (true) { let v : i64 = wait this.x; this.y = v + 100; } }
}
construct {
  let s : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
  let o : mut Outer = new Outer();
  connect(s, o.x);
}
initial { wait 1 tick; s.write(1); wait 3 tick; println("survived"); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `has two drivers`
