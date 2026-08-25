# PassthroughThreeLevels
## Description
Three levels of composition (L3 → L2 → L1) using input passthrough and output forwarding wires: a transaction written at the top arrives at the innermost module and its output flows back out — the _LateSource late-binding chain. 10 becomes 11 through one increment.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class L1 { input x : i64 = 0; output y : i64 = 0; initial { while (true) { let v : i64 = wait this.x; this.y = v + 1; } } }
class L2 { input x : i64 = 0; output y : i64 = 0; construct { let inner : mut L1 = new L1(); connect(this.x, inner.x); connect(inner.y, this.y); } }
class L3 { input x : i64 = 0; output y : i64 = 0; construct { let inner : mut L2 = new L2(); connect(this.x, inner.x); connect(inner.y, this.y); } }
construct { let src : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(8, new __builtin.FifoPolicy.backpressure()); let top : mut L3 = new L3(); connect(src, top.x); }
initial { src.write(10); let r : i64 = wait top.y; println(cast<string>(r)); exit(0); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `11
`
ExpectedStderr: DISCARD
