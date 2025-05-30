## Asynchronization
The penguin-lang is built with asynchronization. You can call a function using `async` keyword, so it will return a IFuture and you can use `await` keyword to wait for the result.
```
	fun foo() -> i32 {
		return 1;
	}

	fun bar() -> i32 {
		val task : IFuture<i32> = async foo();
		println("before wait");
		val a : i32 = wait task;
		println("wait done");
	}
```
However, under the hood, a function can be stateful function or a simple function. If a function is stateful function, it uses `wait` keyword inside, or calls another stateful function.
When calling a stateful function without `async` keyword, the function is actually rewrited to wait for the result of the function.

If a function uses `wait` keyword, or it calls a statefull function, the function itself will be a stateful function.
```
	// simple function
	fun foo() -> i32 {
		return 1;
	}

	// stateful function
	fun bar() -> i32 {
		wait;
		return 2;
	}

	initial {
		foo();	// normal function call

		bar();				// implicit wait 
		wait (async bar());	// equivalent to above line
	}
	wa
```

A `wait` keyword without expression tells penguin-lang runtime to pause the current job and wait for another schedule.

## Threading and Coroutine
One purpose of penguin-lang is to provide a simple and efficient way to write concurrent programs. In most time, programmer only need to focus on the logic of the program, and the runtime will automatically handle the concurrency. Under the hood, penguin-lang uses stackless coroutine to implement asynchronization. This allows the compiler to transform a stateful function into several jobs,
each with its own input, output, and data.

As penguin-lang strictly defines that Value Types are always copied by value, they can always safely passed between threads. Howver this doesnot apply to Reference Types, such as Box. 
So when penguin-lang found a job that only contains value types (or reference types that implements ISynchronized), this job can be dispatched to a different thread. 

Consider a very simple code:
```
var foo : Box<i32> = new Box(0);

fun bar() {
	var i : i32 = foo.get();
	i+=1;
	foo.set(i);
}
```

Although above code is an anti-pattern use of global variable, it's a good example that many programming languages will have data-racing issue when 'bar' is working concurrently and how penguin-lang can handle this case. The Box type provides 'get'/'set' method, which is stateful function, so it will cause 'bar' to be stateful, and be split into three jobs:
```
class bar_job1 {
	var i : i32;
	var foo : Box<i32>;
	fun execute(var this: bar_job1) {
		this.i = this.foo.get();
		// continue with bar_job2
	}
}

class bar_job2 {
	var i : i32;
	fun execute(var this: bar_job2) {
		this.i+=1;
		// continue with bar_job3
	}
}

class bar_job3 {
	var i : i32;
	var foo : Box<i32>;
	fun execute(var this: bar_job3) {
		this.foo.set(this.i);
	}
}
```

You can see that 'bar_job1' & 'bar_job3' has a reference-typed variable `foo`, so they are stick to the same thread (penguin-lang enforce one thread for each global reference-typed variable). 
But 'bar_job2' only has value-typed variable, so 'bar_job2' can be safely dispatched to a different thread.

Note that BabyPenguin (which is a minimal implementation of penguin-lang used to implement EmperorPenguin) is single threaded and uses stackful coroutine. 


## Event Asynchronization

Event must be value-typed and is by default run asynchronously and parallelly. Consider following code:
```
event A : i32;

initial {
	for (i in 0..10) {
		emit A(i);
	}
}

on A(x) {
	print(x as string);
}
```
Possible output can be: `03241...`. This is because penguin-lang compiler see no dependency between event handlers (considered as 'pure'),so penguin-lang runtime is free to re-order or parallelize these event handlers.

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

```
Events can also be dispatcher to different handlers running in multiple threads. This is because event parameters are read-only, so multiple handlers will not cause data race. However, events are not good at collecting results, so we can use a queue. A typicial multiple producers multiple consumers program may look like:
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
		val job : Option<i32> = job_queue.dequeue();
		if (job.is_none()) 
			break;
		else 
			counter.fetch_add(1);
	}
}
```
Above code will generate multiple jobs that visit global reference-typed variable, however they are all `ISynchronized` so they can be safely dispatched to different threads. 
