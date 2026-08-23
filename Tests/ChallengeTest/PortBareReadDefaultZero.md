# PortBareReadDefaultZero
## Description
Design (Q3): a port's initial value is its explicit default or the type zero value — a bare read of a never-driven output yields the type zero deterministically. The output hub carries the type zero as a current()-only seed (it never wakes transaction waiters; `wait m.y` still parks until a real write). Previously BabyPenguin raised runtime error[100] 'port read before any value was delivered'.

## Apply To
* BabyPenguin

## Test Code
```
class M { input x : i64; output y : i64; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
construct { let s : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure()); let m : mut M = new M(); connect(s, m.x); }
initial { let r : i64 = m.y; println(cast<string>(r)); exit(0); }
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `0
`
ExpectedStderr: DISCARD
