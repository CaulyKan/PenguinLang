# Async, Timing and Modules

PenguinLang's concurrency model has three layers: **coroutines** (`async`/`wait`), a **discrete timing model** (a simulation clock in ticks), and **modules** (classes with ports wired together). Together they support ordinary concurrent programs, simulations, and RTL-style hardware descriptions. This page tours all three; the exact rules live in the [Async & Timing Model](../specifications/09_AsyncAndTimingModel.md) and [Modular Programming](../specifications/10_ModularProgramming.md) specifications.

Coroutine features need `--enable-coroutine` on EmperorPenguin (BabyPenguin enables them unconditionally). Programs without `wait`/`async` behave identically with or without the flag.

## Spawning Work: async and wait

`async f(args)` spawns `f` as a concurrent job and returns an `IFuture<T>`. `wait task` parks until the future completes and yields its result:

```penguin
fun work() -> i32 {
    wait 2 tick;
    return 42;
}

initial {
    let task: mut IFuture<i32> = async work();
    println("before wait");
    let a: i32 = wait task;
    println("wait done " + cast<string>(a));
}

initial {
    wait 1 tick;
    println("tick 1");
}
```

This always prints `before wait`, `tick 1`, `wait done 42` in that order: `work` suspends on the clock, the second routine runs to tick 1, then the clock advances to tick 2 and `work` returns.

Calling a suspending function directly is an implicit wait — `bar()` means `wait async bar();`. A bare `wait;` yields for one scheduler round. The scheduler is **cooperative and single-threaded**: a job runs until it suspends (`wait`, channel parking), then the next job runs. There is no preemption and no data racing inside the scheduler; value types are copied across suspension points and reference types are kept alive by the garbage collector.

## Events

An event is a first-class value (`Event<T>`): store it, pass it, emit from anywhere. Consumers are wait loops:

```penguin
let done : mut Event<void> = new Event<void>();

initial {
    println("working");
    done.emit(void);          // broadcast to everyone parked on it
}

initial {
    wait done;
    println("finished");
}
```

Emission is **broadcast**: every routine parked on the event receives the value. A value emitted while nobody is parked is lost (broadcast is not queueing — use a `Fifo` channel when every value must be preserved). `emit` yields one scheduler round after broadcasting so a re-parking loop keeps up with back-to-back emissions.

## The Timing Model

All routines share one discrete simulation clock measured in **ticks**. The scheduler advances the clock only when no job can make progress at the current time. `_sim_now()` reads it:

```penguin
initial {
    wait 3 tick;
    println("c:" + cast<string>(_sim_now()));    // c:3
}
initial {
    wait 1 tick;
    println("a:" + cast<string>(_sim_now()));    // a:1
}
```

`wait n tick;` (or the short form `wait n;`) suspends for `n` ticks. Shorter durations fire first; equal durations fire in scheduling order. A bare `wait;` parks for one scheduler round (one **delta**) without advancing the clock — that is the unit of *zero time*: `wait 0 tick;` lets every runnable job at the current time finish before resuming. When every routine is parked and nothing can wake anything, the program reaches **quiescence** and terminates normally (exit code 0).

`wait` accepts more than durations:

| Form | Wakes when | Value |
|---|---|---|
| `wait;` | one scheduler round passed | — |
| `wait 5 tick;` | the clock advanced 5 ticks | — |
| `wait a == 5;` | condition holds (re-checked every round) | — |
| `wait change(x);` | the watched value differs from its entry sample | new value |
| `wait ev;` | the event emits | delivered payload |
| `wait task;` | the IFuture completes | the result |
| `wait this.port;` | the port delivers a transaction | delivered value |

## Modules: Ports and connect

A class with `input`/`output` port declarations is a **module** — the cross-routine data path. Modules are wired in `construct` blocks with `connect`, and the wiring is statically checked:

```penguin
class Incrementer {
    input x : i64;
    output y : i64;

    initial {
        while (true) {
            let v : i64 = wait this.x;   // consume one input transaction
            this.y.write(v + 1);         // drive the output
        }
    }
}

construct {
    let x : mut i64 = 1;                 // a mut variable becomes a wire net
    let f1 : mut Incrementer = new Incrementer();
    let f2 : mut Incrementer = new Incrementer();
    connect(x, f1.x);                    // variable -> input
    connect(f1.y, f2.x);                 // output -> input
}

initial {
    x = 2;                               // every assignment drives the net
    let out : i64 = wait f2.y;           // 4 — 2 incremented twice
    println(cast<string>(out));
}
```

The rules are RTL-strict, checked at compile time:

* Inside the module: `input` is read/wait-only; `output` is driven with `write` (or assignment `this.y = v`).
* Outside: another module's input is invisible; an output is readable but not drivable.
* Each output has exactly one driving routine; each input is fed by exactly one `connect` (fan-out from one source to many inputs is fine — every consumer gets its own wire).
* `construct` blocks run at elaboration time, before any `initial`. A `connect`'s source can be an output port, a channel, an event, a `mut` variable (an implicit net, as above), or the module's own input (passthrough); the sink is an input port (or a `MultiInput` for fan-in).

## Channels

A channel is a first-class object that is both source and sink of transactions; the buffering **policy** is the concrete channel you choose:

```penguin
let q : mut Fifo<i64> = new Fifo<i64>(2, new FifoPolicy.backpressure());

initial {                       // producer
    let i : mut i64 = 1;
    while (i <= 5) {
        q.write(i);
        println("w " + cast<string>(i));
        i += 1;
    }
}

initial {                       // consumer
    let n : mut i64 = 0;
    while (n < 5) {
        let v : i64 = wait q;
        println("r " + cast<string>(v));
        n += 1;
    }
}
```

The capacity-2 `Fifo` with the backpressure policy suspends the producer when full (`w 1`, `w 2`, then the producer only proceeds after the consumer reads) — end-to-end flow control with no flow-control code. `LatestChannel<T>` is the wire policy: one settled value per scheduler round (for levels/flags, not messages). `close(ch)` wakes all waiters with a runtime error — the supervision-shutdown idiom.

## MultiInput

`MultiInput<T>` accepts N connects — fan-in where the merge policy lives inside the module. `wait this.inputs` takes the next transaction across any source, round-robin:

```penguin
class Sink {
    inputs : mut MultiInput<i64> = new MultiInput<i64>();
    initial {
        while (true) {
            let v : i64 = wait this.inputs;
        }
    }
}
```

## Organizing Larger Programs

Program structure follows the same ideas at software level: **namespaces** and `using` for symbol organization, `.penguins` project files for multi-file builds, and `.penguin-lib` shared libraries (with `export`-marked definitions) for distributing code — see the [Namespace & Project](../specifications/08_NamespaceAndProject.md) specification. On top of the port/channel layer there is also `libpenguin-esl` (`EmperorPenguin/others/libpenguin-esl/`), an RTL modeling library with a two-phase `Clock`, `Reg`, `Pipe`, `Bus`, and `Mem` — the tinyriscv testbench is built with it.

## Where to Go Next

* [Async & Timing Model specification](../specifications/09_AsyncAndTimingModel.md)
* [Modular Programming specification](../specifications/10_ModularProgramming.md)
* [Basic Introduction](./BasicIntroduction.md) for the language tour.
