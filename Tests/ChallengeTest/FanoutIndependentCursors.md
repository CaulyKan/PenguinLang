# FanoutIndependentCursors
## Description
Design (Q4 / round-4 fan-out rule): one output connected to N inputs is a broadcast — every subscriber receives EVERY transaction through its own delivery cursor, in order, whenever it polls. Each write is a transaction; writes in the same delta round collapse to the final value, writes in different rounds are each delivered. The wiring-level `wait a.y` / `wait b.y` direct readers walk the hub's own cursor in order as well, so a consumer that starts waiting late still receives the full ordered stream. Previously the hub's poll jumped its cursor to the latest version, so a late direct waiter saw only the newest value (b1=5, then stall).

## Apply To
* BabyPenguin

## Test Code
```
class Echo { input x : i64; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v; } } }
construct {
  let src : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure());
  let e : mut Echo = new Echo();
  let a : mut Echo = new Echo();
  let b : mut Echo = new Echo();
  connect(src, e.x); connect(e.y, a.x); connect(e.y, b.x);
}
initial {
  src.write(10); src.write(5);
  let a1 : i64 = wait a.y; let a2 : i64 = wait a.y;
  let b1 : i64 = wait b.y; let b2 : i64 = wait b.y;
  println(cast<string>(a1) + " " + cast<string>(a2) + " " + cast<string>(b1) + " " + cast<string>(b2));
  exit(0);
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `10 5 10 5
`
ExpectedStderr: DISCARD
