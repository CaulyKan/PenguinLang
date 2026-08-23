## Asynchronization and Concurrency
Penguin-lang is built with asynchronization and concurrency in mind. You can spawn a function using `async`, which returns an `IFuture`, and use `wait` to get the result.

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

If you call an async function directly (e.g. `bar()`), it is a shorthand for `wait async bar();`
```
	initial {
		bar();				// implicit wait 
		wait (async bar());	// equivalent to above line
	}
```

A `wait` keyword without an expression tells the penguin-lang runtime to pause the current job and wait for another schedule.

## `on` routines

The `on` keyword allows you to create a routine that executes when a certain condition is met. This is a powerful feature for reactive programming.

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

## Threading and Coroutine
One purpose of penguin-lang is to provide a simple and efficient way to write concurrent programs. In most time, programmer only need to focus on the logic of the program, and the runtime will automatically handle the concurrency. Under the hood, penguin-lang uses stackless coroutine to implement asynchronization. This allows the compiler to transform a stateful function into several jobs,
each with its own input, output, and data.

As penguin-lang strictly defines that Value Types are always copied by value, they can always safely passed between threads. Howver this doesnot apply to Reference Types, such as Box. 
So when penguin-lang found a job that only contains value types (or reference types that implements ISynchronized), this job can be dispatched to a different thread. 

For example:
```
let foo : mut Box<i32> = new Box(0);

fun bar() {
	let i : mut i32 = foo.get();
	i += 1;
	foo.set(i);
}
```

Although above code is an anti-pattern use of global variable, it's a good example that many programming languages will have data-racing issue when 'bar' is working concurrently and how penguin-lang can handle this case. The Box type provides 'get'/'set' method, which is stateful function, so it will cause 'bar' to be stateful, and be split into three jobs:
```
class bar {
	i : i32;
	foo : Box<i32>;
	fun step1(this: mut bar) {
		this.i = this.foo.get();
	}
	fun step2(this: mut bar) {
		this.i += 1;
	}
	fun step3(this: mut bar) {
		this.foo.set(this.i);
	}
}
```

You can see that 'step1' & 'step3' has a reference-typed variable `foo`, so they are stick to the same thread (penguin-lang enforce one thread for each global reference-typed variable). 
But 'step2' only has value-typed variable, so 'bar_job2' can be safely dispatched to a different thread.

Note that BabyPenguin (which is a minimal implementation of penguin-lang used to implement EmperorPenguin) is single threaded and uses stackful coroutine. 


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


## Folking
You can use folk keyword to generate parallel initial blocks.
```
folk(5) initial {
	log.info("hello!");
}
```
Above code will print 5 'hello' as expected. You can also use `fork initial:` to automatically get best parallel performance.


## ISynchronized

penguin-lang provides a `ISynchronized` interface to mark a reference-typed variable as thread-safe. Typical types are Atmoic, Mutex, RWLock and ConcurrentQueues.

For example:
```
let counter : mut Atomic<i32> = new Atomic(0);
let job_queue: mut ConcurrentQueue<i32> = new ConcurrentQueue<i32>();

initial {
	for (let mut i : i32 in range(0, 10)) {
		job_queue.enqueue(i);
	}
	wait counter.load() == 10;
}

folk initial {
	while (true) {
		let job : Option<i32> = job_queue.dequeue();
		if (job is Option<i32>.None)
			break;
		else
			counter.fetch_add(1);
	}
}
```
All these global reference-typed variables are `ISynchronized`, so jobs can be safely dispatched to different threads.
