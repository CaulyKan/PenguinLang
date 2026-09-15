# PipeChannel
## Description
esl.Pipe<T> — the two-phase pipeline channel: a `write` during evaluate lands in the in-slot only; the esl.Clock's commit phase publishes it (exactly one cycle of delay, like a pipeline register); a cycle with no write publishes a BUBBLE (`try_poll` -> none). The consumer polls non-blocking on its own clock edge, cycle 1 is the Clock's (lost) reset edge — no emit, the pipe publishes its reset bubble; evaluate rounds run at cycles 2..6, so the trace is one entry per evaluate: '-' (reset bubble), 0, 1, '-' (the deliberate no-write bubble at cycle 4), 3 (recovery after the bubble). `published()`/`current()` expose the committed slot WITHOUT consuming — the registered view the arbitration/control readers use — so after cycle 6's commit it reads the value written during cycle 6 (4), which the consumer has not yet polled. Wiring: clock fan-out via `connect(clk.evt, m.clk)` (Event<i64> -> input port), the pipe itself `connect(p, cons.din)` (channel-like source binds directly). Pass3-only: esl.Clock is backed by std.Vector (vector.penguin on the Compile.Args line).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
using esl;

class Producer {
    input clk : i64;
    p : mut Pipe<i64>;

    fun new(mut this, p : mut Pipe<i64>) {
        this.p = p;
    }

    initial {
        let i : mut i64 = 0;
        while (true) {
            let cyc : i64 = wait this.clk;
            if (i != 2) {
                // cycle 3 (i == 2) deliberately writes nothing: bubble.
                this.p.write(i);
            }
            i = i + 1;
        }
    }
}

class Consumer {
    input clk : i64;
    input din : i64;
    trace : mut StringBuilder = new StringBuilder();

    fun report(this) -> string { return this.trace.to_string(); }

    initial {
        while (true) {
            let cyc : i64 = wait this.clk;
            let v : Option<i64> = this.din.try_poll();
            if (v.is_some()) {
                this.trace.append(cast<string>(v.some));
            } else {
                this.trace.append("-");
            }
            this.trace.append(",");
        }
    }
}

class StopAt6 {
    clk : Clock;
    cons : Consumer;
    p : mut Pipe<i64>;

    fun new(mut this, clk : mut Clock, cons : mut Consumer, p : mut Pipe<i64>) {
        this.clk = clk;
        this.cons = cons;
        this.p = p;
    }

    impl ICycleHook {
        fun after_commit(this) -> bool {
            if (this.clk.cycles >= 6) {
                println("trace=" + this.cons.report());
                let cur : Option<i64> = this.p.published();
                if (cur.is_some()) {
                    println("cur=" + cast<string>(cur.some));
                } else {
                    println("cur=none");
                }
                return true;
            }
            return false;
        }
    }
}

construct {
    let clk : mut Clock = new Clock();
    let p : mut Pipe<i64> = new Pipe<i64>(clk);
    let prod : mut Producer = new Producer(p);
    let cons : mut Consumer = new Consumer();
    connect(clk.evt, prod.clk);
    connect(clk.evt, cons.clk);
    connect(p, cons.din);
    let hook : mut StopAt6 = new StopAt6(clk, cons, p);
    clk.set_hook(hook);
}
```

## Compile
Args: `EmperorPenguin/others/libpenguin-esl/Pipe.penguin EmperorPenguin/others/libpenguin-esl/Clock.penguin EmperorPenguin/others/libpenguin-esl/Reg.penguin EmperorPenguin/std/penguin/vector.penguin --enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `trace=-,0,1,-,3,
cur=4
`
ExpectedStderr: DISCARD
