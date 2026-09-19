# Modular Programming

This chapter documents the RTL-style communication layer — module ports, `construct` wiring blocks, first-class channels, broadcast events, and the scheduling semantics underneath them (delta cycles, settle points, quiescence) — and the software modularization layer: `.penguin-lib` shared libraries. The vocabulary is deliberately small:

- **`wait` is the only wakeup primitive.** It accepts a duration (`wait 5 tick`), a condition (`wait a == 5`), an edge (`wait change(x)` — parks until the watched value differs from its entry sample, yields the new value), an event (`let v : T = wait ev`), a future (`let v : T = wait task`), or a port (`let v : T = wait this.x`). Every form that carries a payload returns the delivered value.
- **There are no `event`/`emit`/`on` keywords.** Broadcast is the first-class `Event<T>` value; subscription is a wait loop. There is no `poll`.
- **The compiler owns three things**: port declarations, the `connect` topology checks, and the scheduling/delivery primitives. Every buffering, routing or arbitration *policy* is library code (channels implementing `ISource`/`ISink`).

## Events (anonymous broadcast)

An event is an ordinary value — store it in a global, a class field or a local, pass it to free functions, emit from anywhere:

```penguin
let clk : mut Event<i32> = new Event<i32>();

fun deep(c : mut Event<i32>) { c.emit(1); }    // free functions can emit

initial {
    while (true) {
        let v : i32 = wait clk;
        println(cast<string>(v));
    }
}
```

Void-payload events (`Event<void>`) carry no value; emit them with the void literal: `ev.emit(void);`, and `wait ev;` wakes without returning anything.

Semantics — three rules:

1. **Broadcast**: every routine parked on the event receives the value. Each `wait ev` parks on its own one-slot subscription, so N waiters see N deliveries.
2. **Lost when nobody listens**: a value emitted while nobody is parked is gone — broadcast is not queueing. Use a `Fifo` channel for store-and-forward.
3. **One slot per wait**: two emits before the waiter re-parks collapse to the last value (wire semantics). An `emit` yields one delta after broadcasting, so a waiter loop keeps up with back-to-back emits.

An event can also be **wired into module ports** — `connect(ev, f.x)` grants each connected input a permanent wire fed by every emit, exactly like an output port's fan-out; parked `wait ev` subscribers and connected wires all receive the same emits. Each connected wire carries its own delivery cursor: emits in different scheduler rounds are each delivered (in order) to every consumer whenever it polls, while emits in the same round collapse to the final value.

## Ports

A class declared with ports is a module. Ports are the official cross-routine data path:

```penguin
class Foo {
    input x : i64;
    output y : i64;

    initial {
        while (true) {
            let v : i64 = wait this.x;   // consume one input transaction
            this.y = v;                  // assignment sugar for y.write(v)
        }
    }
}
```

**Permission matrix (RTL-strict):**

|              | inside the module       | outside the module |
|--------------|-------------------------|--------------------|
| `input x`    | read / `wait x` only    | no read, no write  |
| `output y`   | `y.write(v)` / `y = v`  | read only          |

- Writing an input, writing someone else's output, or driving an output from two places are compile errors — in **every syntactic form**: plain assignment (`this.x = v`), method call (`this.x.write(v)`, `m.y.write(v)`) and compound assignment (`this.x += 1`) are all rejected. The only way to drive an input is `connect`. Two *routines* writing the same output is equally a compile error (one driving routine per output; merge streams through a channel).
- Reading another module's input — bare read (`m.x`) or wait (`wait m.x`) — is a compile error; inputs are only visible inside their own module.
- Ports carry no `mut` modifier — the matrix already fixes every read/write rule.
- Defaults and initial values: a port's initial value is its explicit default **or the payload type's zero value**.
  - `input x : i64 = 0;` binds a constant source (the default is a *level*, readable via bare read; it never delivers a transaction).
  - `output line : bool = true;` (the UART idle-level case) is a weak deliverable seed: a first `wait line` wakes immediately with it, and any real write supersedes it.
  - An output *without* a default carries the type zero as a current-only seed — bare reads return it deterministically, but `wait port` parks until the module actually writes.
- **Topology is statically checked** (`error[E_WIRING]`): connecting two sources to one input, leaving a default-less input of a construct-instantiated module unconnected, or driving an output from body code *and* a construct wire are all compile errors. Modules instantiated *dynamically* (a `new` outside construct blocks) skip the static audit — an unbound input there parks gracefully at runtime (blocked, not crashed).
- A **bare port read** (`let v : i64 = s.out;`) is a **settle point**: it first parks one delta at a time until the current simulation time's propagation has settled (the previous scheduler round carried no transaction activity and the current one is quiet so far), then samples the channel's *current slot*. The canonical example — `x = 2; println(f2.y)` — prints the propagated `2`, not the stale slot. Reading a port inside a `construct` block is a compile error (a settle point is a suspension, and constructs must not wait). `wait port` consumes the next transaction; `wait <condition>` re-evaluates every scheduler round through the settle read (`wait s.line == false` is the Verilog `wait()` idiom). `wait change(port)` is the one exception: it samples the **raw level** every round without settling — edge detection must see single-round pulses. A *plain variable* read is an ordinary read of the latest assignment (see [Async & Timing Model](./09_AsyncAndTimingModel.md)).

## connect and construct

`connect(source, sink)` wires topology; it is legal only inside a `construct` block. Top-level `construct` blocks run at elaboration time, before any `initial`; class-level `construct` runs at `new` (after the constructor, before the instance's initials spawn):

```penguin
construct {
    let x : mut i64 = 1;
    let f1 : mut Foo = new Foo();
    let f2 : mut Foo = new Foo();
    connect(x, f1.x);
    connect(f1.y, f2.x);
}

initial {
    x = 2;                             // every assignment also drives the net
    let out : i64 = wait f2.y;         // 2
}
```

- A construct block's `let`s become ordinary bindings of the enclosing scope (top level) or stay local to the wiring block (class level).
- **Sources**: an output port, any channel expression, an `Event` (every emit feeds the input through its own permanent wire — broadcast and connected wires coexist), a `mut` variable (implicit net, see below), or this module's own input (passthrough, see below). **Sinks**: an input port (member access `f.x` / `this.x`), or a `MultiInput` (a port field or a bare `let mi` binding).
- Connecting one output to N inputs fans out: every connected input gets its *own* wire with an independent delivery cursor — each consumer receives **every transaction, in order, whenever it polls**; a late consumer never loses transactions. Multiple writes within one scheduler round collapse to the final value (a wire settles to one value per delta).
- Output **passthrough** composes hierarchies: `connect(inner.y, this.y)` makes the composer's output literally the inner hub (one driver, transparent forwarding).
- Input **passthrough** (`connect(this.x, inner.x)`) works with late external wiring: the construct rebinds the input field to a relay, and the *outer* connect (which necessarily runs later) binds the real source into that relay — no hand-written forwarding process. v1 restriction: one passthrough line per input (fan-out from a passed-through input is not supported), and a module body that also *consumes* the same input competes with the relay for transactions.

### Implicit nets (`mut` variable sources)

`connect(x, f.x)` where `x` is a `mut` variable turns the variable into a wire net:

- Works for top-level construct `let`s (explicit `mut T` type required) and for class fields (`connect(this.baud, inner.clk)` — the shared-config-register shape); the net hub is a hidden field, per instance.
- The variable's value at connect time is the net's deliverable seed (a connected input's first `wait` wakes with it; the first real assignment supersedes it); afterwards every assignment to the variable — plain, compound (`x += 1`), from any routine — also writes the hidden hub, so connected inputs observe the new value.
- Net reads stay plain variable reads (ordinary reads of the current value anywhere the variable is visible). Fan-out works like an output port: each connect subscribes its own wire.

## Channels

A channel is a first-class object that is both a source and a sink of transactions (`ISource<T>` + `ISink<T>`). Modules see only the interfaces; the *policy* lives in the concrete channel the wiring chose:

| Channel          | Policy                                                     |
|------------------|------------------------------------------------------------|
| `Fifo<T>(cap, policy)` | every value is delivered, in order; `Backpressure` suspends the writer when full (end-to-end flow control with zero flow-control code), `Drop` discards on full |
| `LatestChannel<T>` | wire: one settled value per scheduler round — writes in the same round collapse to the final value, writes in different rounds are each delivered in order to every consumer cursor |
| `MultiInput<T>`  | dynamic fan-in — see below                                 |

Channels are consumed directly (`q.write(v)`, `let v : T = wait q;`, `q.try_poll() -> Option<T>`) or used as a `connect` **source** feeding an input port (`connect(q, f.x)`). A channel is not a valid connect *sink* — the sink side is always an input port or a `MultiInput`.

Two inversions define the territory of wire vs FIFO:

- **wire (`LatestChannel`) is for levels** — "the newest value is the truth": status flags, configuration registers, handshake lines. It is *not* a message tool: two frames written in the same scheduler round collapse into protocol corruption (frames written in different rounds each arrive, but relying on that timing is fragile — the wire contract is one settled value per round).
- **FIFO is for messages** — every line, frame or command that must arrive, exactly once, in order.

Every write is a transaction — writing the same value twice delivers twice (no Verilog-style value dedup). `write(v)` may suspend (backpressure); `try_write(v) -> bool` is the non-blocking escape hatch. `y = v` on an output desugars to `y.write(v)`, so output assignment is a potential suspension point — document it next to `wait` when reasoning about scheduling.

**close is supervision shutdown**: `close(ch)` wakes every pending and future waiter with a runtime error ("channel closed"); writing a closed channel raises as well. Closing one top-level channel tears down the whole module tree still waiting on it — each module's `try/catch` decides whether that is an orderly shutdown (see `ExceptionTest`).

## MultiInput

`MultiInput<T>` is a port-like object accepting N connects — fan-in where the merge policy lives *inside* the module:

```penguin
class Sink {
    inputs : mut MultiInput<i64> = new MultiInput<i64>();
    initial {
        while (true) {
            let v : i64 = wait this.inputs;   // next transaction across ANY source
        }
    }
}

construct {
    let s : mut Sink = new Sink();
    connect(producer_a.out, s.inputs);
    connect(producer_b.out, s.inputs);
}
```

`wait mi` scans the registered sources round-robin (a busy source cannot starve the others). For a custom policy — priority, per-source handling — iterate the source views and probe without blocking:

```penguin
for (let src : mut ISource<i64> in this.inputs.iter()) {
    let v : Option<i64> = src.try_poll();
    ...
}
```

The alternative pattern, a *wirer-held shared Fifo* that multiple producers write, keeps the policy at the wiring end; `MultiInput` self-documents the fan-in contract on the module itself. Both are legal.

## Libraries (.penguin-lib)

Beyond single-process modules, a program can be split into shared libraries. A `.penguin-lib` is a native shared object (`.so`/`.dll`) with a JSON symbol table appended after the binary, produced by building with a `.penguin-lib` output name:

```bash
# build a library (export-marked defs become its public surface)
emperor libsrc.penguin -o libfoo.penguin-lib

# compile a consumer against it, then link exe+lib together
emperor app.penguin --lib libfoo.penguin-lib -o app
```

Rules in brief:

* **Export**: only `export`-marked definitions (see [Namespace & Project](./08_NamespaceAndProject.md)) plus their referenced-type closure, every global variable (re-initialized by the consumer), every top-level `impl X for Y` edge, and **verbatim source** for files containing template/meta constructs (so consumers can monomorphize new generic instances locally) enter the library's metadata. Everything else is private to the `.so`.
* **Declare-not-define**: a consumer compiles against the library's declarations and calls into the `.so` at runtime. Specializations the library already shipped are reused; new ones are instantiated in the consumer.
* **Relocatable pairs**: the executable carries the C runtime (and optional JIT), binds the lib's runtime symbols from itself via `-rdynamic`, and finds the `.penguin-lib` beside it via `$ORIGIN` rpath — an exe + `.penguin-lib` pair can be moved together.
* Libraries require the dynlib-capable compilers (EmperorPenguin Pass3 and later builds). The full metadata format and consumption pipeline are documented in the implementation notes ([EmperorPenguin Dynlib](../impl-notes/28_EmperorPenguinDynlib.md)).

## Termination and pathological topology

- **Termination = all initial routines finished + scheduler quiescence.** Quiescence itself is not an exit — a server parked waiting for external input is a legal final state in the design — and since the LSP runtime landed, that state is real: `__builtin._fd_wait_read(fd)` / `_fd_wait_write(fd)` park the current coroutine on file-descriptor readiness (stdin/stdout for the stdio-based language server, any non-blocking fd). While at least one fd waiter is parked the scheduler never exits at quiescence — it blocks in `poll()` over the registered descriptors and readiness of any of them injects the next delta round (EOF wakes read waiters; the following `_read_fd` returns `""`). Programs that never park on an fd keep the v1 behavior: a dead-still program simply ends. Use `exit()` for an explicit `$finish`.
- A condition that never becomes true (`wait a == 99`) contributes identical scheduler rounds and ends at quiescence, exactly like a parked channel waiter.
- Zero-delay oscillation loops burn delta rounds inside one simulation time; the scheduler aborts with a live-lock error after its round budget. Converging combinational loops (no new transactions) are legal.
