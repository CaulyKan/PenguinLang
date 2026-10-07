# Async & Timing Model

Penguin-lang is built with asynchronization and concurrency in mind: `async` spawns a function as a concurrent job returning an `IFuture`, `wait` is the single suspension/wakeup primitive, and a discrete simulation clock gives programs control over how time elapses — useful for game logic, HDL simulation, and scientific computation.

Concurrency is enabled at compile time with the `--enable-coroutine` flag on EmperorPenguin (BabyPenguin enables the features unconditionally). Programs without `wait`/`async` produce the same output with or without the flag.

## async and IFuture

```penguin
fun test() -> i32 {
    wait; // simulate some async work
    return 1;
}

initial {
    let task : mut IFuture<i32> = async test();
    println("before wait");
    let a : i32 = wait task;
    println("wait done");
    print(cast<string>(a));
}
```
This outputs:
```
before wait
wait done
1
```

`async expr` requires a function call; the callee may itself be async or not. The result is an `IFuture<T>` (created immediately, completed when the job returns). `async` on a static member call is rejected.

## Stateful Functions and Implicit Wait

PenguinLang automatically identifies whether a function is stateful: a function that uses `wait` or `yield`, or calls another stateful function, is a stateful (suspending) function; the `async`/`!async` specifiers force or suppress this.

Calling a stateful function directly is a shorthand for `wait async f();`:
```
initial {
	bar();				// implicit wait
	wait (async bar());	// equivalent to the line above
}
```

A `wait` keyword without an expression pauses the current job and reschedules it for the next scheduler round.

## The wait Forms

| Form | Wakes when | Value |
|---|---|---|
| `wait;` | one scheduler round (one delta) passed | — |
| `wait n;` | the simulation clock advanced `n` ticks | — |
| `wait <event>;` | the event emits | delivered payload |
| `wait <IFuture>;` | the future completes | the result |
| `wait <channel>;` | the channel delivers a transaction | delivered value |
| `wait <port>;` | the port delivers a transaction | delivered value (see [Modular Programming](./10_ModularProgramming.md)) |

The operand must be an integer (a timer), an `Event`, an `IFuture`, a channel, or a port. A bool operand and a `change(x)` operand are compile errors (`E_TYPE_MISMATCH`) — a condition has no wake source (the writer of the condition cannot know who waits on it); wait on an Event, a channel, a future or a port transaction instead.

## Scheduling Model

The runtime schedules jobs cooperatively on a single thread: each suspension point (`wait`, event parking, channel polling) yields control to the scheduler, which resumes the next runnable job. There is no preemption and no data racing within a program.

* BabyPenguin executes stateful functions on its VM, interleaving jobs at their suspension points.
* The EmperorPenguin native runtime runs the scheduler on the main thread using fibers (POSIX/Windows), switching between the scheduler and each suspended job.

Value types are copied at every value-model boundary, so they are always safe to move across suspension points; reference-type values stay alive via the garbage collector.

## Events

Events are first-class values (`Event<T>`, payload must be value-typed) consumed by wait loops:
```
let A : mut Event<i32> = new Event<i32>();

initial {
	for (let mut i : i32 in range(0, 10)) {
		A.emit(i);
	}
}

initial {
	while (true) {
		let x : i32 = wait A;
		print(cast<string>(x));
	}
}
```
The subscription loop receives events in emission order. An `emit` yields one delta after broadcasting, so a re-parking loop keeps up with back-to-back emissions; a value emitted while nobody is parked is lost (broadcast, not queueing — use a `Fifo` channel when every value must be preserved regardless of consumer pacing, see [Modular Programming](./10_ModularProgramming.md)).

Multiple parked wait loops all receive every emission (broadcast); within one delta they wake in spawn order under the cooperative scheduler. Void-payload events (`Event<void>`) are emitted with the void literal (`ev.emit(void);`) and wake without returning a value.

## Timing Model

Penguin-lang uses a discrete simulation clock measured in **ticks**. All routines share the same simulation time; the scheduler advances it only when no job can make progress at the current time.

The current simulation time is available through the `_sim_now()` builtin:
```
initial {
	println(cast<string>(_sim_now()));   // 0
	wait 3;
	println(cast<string>(_sim_now()));   // 3
}
```

### Waiting for Duration
`wait <n>;` suspends the routine until the simulation clock has advanced by `n` ticks, where `<n>` is any integer expression — a literal, a variable (any integer width; narrower ints are cast to the i64 deadline unit) or a computation. Timers with shorter durations fire first; equal durations fire in scheduling order:
```
initial {
	wait 1;
	println("A");
}
initial {
	wait 2;
	println("B");
}
```
This prints `A` then `B`.

### Zero-Time and Settling
`wait 0;` does not advance simulation time. It lets the scheduler finish every runnable job at the current time — updating assignments and propagation — and then reschedules the current routine:
```
let a : mut i32 = 0;

fun set_a() {
	a = 2;
}

initial {
	let f = async set_a();
	wait 0;
	println(cast<string>(a));   // 2
}
```
A bare `wait;` (no expression) parks the routine for one scheduler round — one delta — without touching the tick counter. You MUST NOT rely on zero-time waits to observe value assignments on other routines; use an event (`Event<T>` broadcast) or a port/channel connection (see [Modular Programming](./10_ModularProgramming.md)).

### Variable Assignment and Reads
A plain variable is ordinary storage: a read observes the latest assignment — immediately for subsequent reads in the same routine, and for other routines once the assigning routine has executed it.
```
let a : mut i32 = 0;				// initial value, assigned before start of simulation
initial {
	a = 2;
	println(cast<string>(a));		// prints 2
}
```
Port reads follow different rules: a bare port read is a **settle point** — it first lets the current time's propagation settle, then samples the channel's current slot (see [Modular Programming](./10_ModularProgramming.md)).

### Quiescence
When every routine is parked and no timer, event, or channel can wake anything, the program has reached quiescence and terminates normally — with exit code 0. A wait that never wakes is a legal final state:
```
let q : mut Fifo<i64> = new Fifo<i64>(1, new FifoPolicy.backpressure());

initial {
	println("start");
	let v : i64 = wait q;		// no producer ever writes; parks forever
	println("never");
}
```
This prints `start` and exits with code 0. Programs can also park on file-descriptor readiness (`__builtin._fd_wait_read/_fd_wait_write`, used by the stdio-based language server) — while at least one fd waiter is parked the scheduler blocks in `poll()` instead of exiting.

## Example
Following is an example of playing chess between two players, which makes good use of the timing model.
```
fun move_black() {
	...
}
	
fun move_white() {
	...
}

enum victory_result {
	white;
	black;
	none;
}
	
fun	check_victory() -> victory_result {
	...
}

initial {
	while true {
		move_black();
		wait 2;
	}
}
		
initial {
	wait 1;
	while true {
		move_white();
		wait 2;
	}
}

initial {
	while (true) {
		wait 1;
		let r : victory_result = check_victory();
		if (r is victory_result.none) {
			continue;
		} else if (r is victory_result.black) {
			println("black wins!");
			exit(0);
		} else {
			println("white wins!");
			exit(0);
		}
	}
}
```
