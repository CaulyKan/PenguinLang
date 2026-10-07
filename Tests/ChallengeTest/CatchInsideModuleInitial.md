# CatchInsideModuleInitial
## Description
try/catch works inside a class-body initial routine (yield-path interpreter): the module consumes one transaction, then a channel close raises through the parked wait, is caught inside the module loop which prints and breaks. Exception propagation across suspension points in module context.

UPDATED (M0 BabyPenguin scheduler alignment): same seed race as PassthroughThreeLevels — `output y : i64 = 0` is a deliverable seed and do_wait polls FIRST, so `wait m.y` returns the seed 0 at time 0 (classic printed got:1 because its park-first pacing skipped the seed). The close-then-catch teardown half is unchanged. Verified byte-identical on BabyPenguin, pass1+mt and pass3+mt.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class M {
    input x : i64 = 0;
    output y : i64 = 0;
    initial {
        while (true) {
            try { let v : i64 = wait this.x; this.y = v; }
            catch (e : __builtin.RuntimeError) { println("module-caught"); break; }
        }
    }
}
construct { let s : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(4, new __builtin.FifoPolicy.backpressure()); let m : mut M = new M(); connect(s, m.x); }
initial { s.write(1); let got : i64 = wait m.y; println("got:" + cast<string>(got)); s.close(); wait 1; exit(0); }
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `got:0
module-caught
`
ExpectedStderr: DISCARD
