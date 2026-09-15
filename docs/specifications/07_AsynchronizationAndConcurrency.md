## Asynchronization and Concurrency
Penguin-lang is built with asynchronization and concurrency in mind. You can spawn a function using `async`, which returns an `IFuture`, and use `wait` to get the result.

Concurrency is enabled at compile time with the `--enable-coroutine` flag (accepted by BabyPenguin and EmperorPenguin with coroutine support). Programs without `wait`/`async` produce the same output with or without the flag.

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
This will output:
```
before wait
wait done
1
```

PenguinLang adopts a full stackless coroutine model, and will automatically identify if a function is 'async' or not. If a function uses `wait`, or calls a stateful function, the function itself will be a stateful function.

If you call a stateful function directly (e.g. `bar()`), it is a shorthand for `wait async bar();`
```
	initial {
		bar();				// implicit wait
		wait (async bar());	// equivalent to above line
	}
```

A `wait` keyword without an expression tells the penguin-lang runtime to pause the current job and wait for another schedule.

## Waiting for conditions
`wait <condition>` parks the routine and re-checks the condition every scheduler round until it holds (see `02_ExecutionFlowAndEvents.md` for the event form and `08_TimingModel.md` for edge-sensitive `wait change`):
```penguin
let a : mut i32 = 0;

initial {
	for (let i : i32 in range(0, 10)) {
		a = i;
	}
}

initial {
	wait a == 5;
	println("a is 5");
}
```

## Scheduling Model
One purpose of penguin-lang is to provide a simple and efficient way to write concurrent programs. The runtime schedules jobs cooperatively on a single thread: each suspension point (`wait`, event parking, channel polling) yields control to the scheduler, which resumes the next runnable job. There is no preemption and no data racing within a program.

* BabyPenguin executes stateful functions on its VM, interleaving jobs at their suspension points.
* The EmperorPenguin native runtime runs the scheduler on the main thread using fibers (POSIX/Windows), switching between the scheduler and each suspended job.

Value types are copied at every value-model boundary, so they are always safe to move across suspension points; reference-type values stay alive via the garbage collector.

## Event Asynchronization
Events are first-class values (`Event<T>`, payload must be value-typed) consumed by wait loops. For example:
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
The subscription loop receives events in emission order. An `emit` yields one delta after broadcasting, so a re-parking loop keeps up with back-to-back emissions; a value emitted while nobody is parked is lost (broadcast, not queueing — use a `Fifo` channel when every value must be preserved regardless of consumer pacing, see `11_PortsChannelsEvents.md`).

Multiple parked wait loops all receive every emission (broadcast); within one delta they wake in spawn order under the cooperative scheduler.

## Channels and Ports
Buffered communication between routines uses the channel library — `Fifo<T>` (store-and-forward with backpressure policy), `LatestChannel<T>`, `MergeChannel<T>`, and `MultiInput<T>` — and RTL-style modules connect through `input`/`output` ports wired in `construct` blocks with `connect`. Both are covered in `11_PortsChannelsEvents.md`.
