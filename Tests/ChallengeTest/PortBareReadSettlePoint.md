# PortBareReadSettlePoint
## Description
Design (Q1): a port read is a settle point — after 'x = 2' the bare read of f2.y first lets the current time's propagation settle (f1 forwards 2, f2 forwards 2), then samples the slot: the canonical Q1 example's println form prints 2. Ordinary variable reads keep their next-tick semantics; only port reads settle. (Historical note: `wait change(port)` used to sample the raw level every round — that form is REMOVED in the concurrency redesign M0; this test's `wait this.x` is a transaction wait, which was never a settle point.)

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Foo { input x : i64; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
construct {
  let x : mut i64 = 1;
  let f1 : mut Foo = new Foo();
  let f2 : mut Foo = new Foo();
  connect(x, f1.x); connect(f1.y, f2.x);
}
initial { x = 2; println(cast<string>(f2.y)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `2
`
ExpectedStderr: DISCARD
