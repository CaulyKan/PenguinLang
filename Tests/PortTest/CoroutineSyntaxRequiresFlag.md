# CoroutineSyntaxRequiresFlag
## Description
RTL-port/concurrency syntax under --disable-coroutine is a compile error on every EmperorPenguin native pass: the gate reports E_UNSUPPORTED ("requires the --enable-coroutine option") instead of silently dropping or partially compiling. Verified shape: a port declaration plus a `wait <expr> tick` in an initial. The flag defaults ON since the std-dynlib split (2026-09-19), so the gate is exercised through the explicit --disable-coroutine form. BabyPenguin is not in Apply To (its VM implements the feature unconditionally).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class M {
    input x : i64;
    output y : i64 = 0;
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.y = v;
        }
    }
}
construct {
    let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure());
    let m : mut M = new M();
    connect(q, m.x);
}
initial {
    wait 1 tick;
    println("never");
}
```

## Compile
Args: `--disable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `requires the --enable-coroutine option`

