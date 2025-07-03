## Asynchronization and Concurrency
Penguin-lang is built with asynchronization and concurrency in mind. You can spawn a function using `async`, which returns an IFuture, and use `wait` to wait for the result.
```
	fun foo() -> i32 {
		return 1;
	}

	fun bar() -> i32 {
		val task : IFuture<i32> = async foo();
		println("before wait");
		var a : i32 = wait task;
		println("wait done");
	}
```

PenguinLang adopts a full stackless coroutine model, and will automatically identify if a function is 'async' or not. If a function uses `wait`, or calls a stateful function, the function itself will be a stateful function.

If you call an async function directly (e.g. `bar()`), it is a shorthand for `wait async bar();`
```
	initial {
		bar();				// implicit wait 
		wait (async bar());	// equivalent to above line
	}
```

A `wait` keyword without expression tells penguin-lang runtime to pause the current job and wait for another schedule.

## Threading and Coroutine
One purpose of penguin-lang is to provide a simple and efficient way to write concurrent programs. In most time, programmer only need to focus on the logic of the program, and the runtime will automatically handle the concurrency. Under the hood, penguin-lang uses stackless coroutine to implement asynchronization. This allows the compiler to transform a stateful function into several jobs,
each with its own input, output, and data.

As penguin-lang strictly defines that Value Types are always copied by value, they can always safely passed between threads. Howver this doesnot apply to Reference Types, such as Box. 
So when penguin-lang found a job that only contains value types (or reference types that implements ISynchronized), this job can be dispatched to a different thread. 

For example:
```
var foo : Box<i32> = new Box(0);

fun bar() {
	var i : i32 = foo.get();
	i += 1;
	foo.set(i);
}
```

Although above code is an anti-pattern use of global variable, it's a good example that many programming languages will have data-racing issue when 'bar' is working concurrently and how penguin-lang can handle this case. The Box type provides 'get'/'set' method, which is stateful function, so it will cause 'bar' to be stateful, and be split into three jobs:
```
class bar {
	var i : i32;
	var foo : Box<i32>;
	fun step1(var this: bar) {
		this.i = this.foo.get();
	}
	fun step2(var this: bar) {
		this.i += 1;
	}
	fun step3(var this: bar) {
		this.foo.set(this.i);
	}
}
```

You can see that 'step1' & 'step3' has a reference-typed variable `foo`, so they are stick to the same thread (penguin-lang enforce one thread for each global reference-typed variable). 
But 'step2' only has value-typed variable, so 'bar_job2' can be safely dispatched to a different thread.

Note that BabyPenguin (which is a minimal implementation of penguin-lang used to implement EmperorPenguin) is single threaded and uses stackful coroutine. 


## Event Asynchronization
Events must be value-typed and are by default run asynchronously and in parallel. For example:
```
event A : i32;

initial {
	for (var i : i32 in range(0, 10)) {
		emit A(i);
	}
}

on A(x) {
	print(x as string);
}
```
The output order is uncertain, because the runtime is free to parallelize event handlers if there is no dependency.

To ensure the order of event handlers, you can use `!pure` keyword to mark the event handler as 'pure', which means it has no side effect and is not a 'pure function'. 
```
on !pure A(x) {
	print(x as string);   // guaranteed to receive events in order
}
```

However, the compiler can automatically detect if a function is pure or not, such as visiting a mutable variable or calling a non-pure function.
```
on A(x) {
	global_var = x;
	print(x as string);   // guaranteed to receive events in order
}
```

Another way is to use initial and wait syntax, the order of events are guaranteed, however not all events are guaranteed to be received, because `wait` only waits for next event.
```
initial {
	while (true) {
		val x : i32 = wait A;  // guaranteed to be receive events in order
		print(x as string);
	}
}
```


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
var counter : Atomic<i32> = new Atomic(0);
var job_queue: ConcurrentQueue<i32> = new ConcurrentQueue<i32>();

initial {
	for (var i : i32 in range(0, 10)) {
		job_queue.enqueue(i);
	}
	wait counter.load() == 10;
}

folk initial {
	while (true) {
		const job : Option<i32> = job_queue.dequeue();
		if (job is Option<i32>.None)
			break;
		else
			counter.fetch_add(1);
	}
}
```
All these global reference-typed variables are `ISynchronized`, so jobs can be safely dispatched to different threads.
