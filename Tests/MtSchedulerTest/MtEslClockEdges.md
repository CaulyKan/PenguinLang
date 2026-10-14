# MtEslClockEdges
## Description
libpenguin-esl under the mt scheduler — the M0-migration sentinel. Two pieces make the two-phase ESL clock work on the deferred semantics: (1) esl.Clock settles `while (__builtin.__sched_settling() > 0) wait 0;` before commit_all (the classic single wait-0 beat assumed zero-latency wires; deferred wires need one beat per request-response chain depth — the probe is > 0 while wire commits are pending or a woken module is still queued); (2) the futures list in the C kernel carries its own link field (fnext) — a timer-woken coroutine's run-queue push used to clobber the shared qnext, orphaning every coroutine parked behind it (this test's module waited forever; tinyriscv timed out at 2M cycles before the fix). Verified up to the full tinyriscv model: identical output and exactly 48614 cycles under mt. This test pins the minimal shape: a module observing 3 clock edges via a connected port wire.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
using esl;

class M {
    input clk : i64;
    initial {
        let n : mut i64 = 0;
        while (n < 3) {
            let c : i64 = wait this.clk;
            println("edge " + cast<string>(c));
            n += 1;
        }
    }
}

class Stop4 {
    impl ICycleHook;
    clk : Clock;
    fun new(mut this, clk : mut Clock) { this.clk = clk; }
    impl ICycleHook {
        fun after_commit(this) -> bool {
            return this.clk.cycles >= 4;
        }
    }
}

construct {
    let clk : mut Clock = new Clock();
    let m : mut M = new M();
    connect(clk.evt, m.clk);
    clk.set_hook(new Stop4(clk));
}
```

## Compile
Args: `EmperorPenguin/others/libpenguin-esl/Clock.penguin EmperorPenguin/others/libpenguin-esl/Reg.penguin EmperorPenguin/std/penguin/vector.penguin --enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `edge 1
edge 2
edge 3
`
ExpectedStderr: DISCARD
