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
```

A `wait` keyword without expression tells penguin-lang runtime to pause the current job and wait for another schedule.

## Event Asynchronization

Event is by default run asynchronously. Consider following code:
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
var global_var : i32 = 0;
on A(x) {
	global_var = x;
	print(x as string);   // guaranteed to receive events in order
}
```

Another way is to use initial and wait syntax, you can also ensure the order of event handlers.
```
initial {
	while (true) {
		val x : i32 = wait A;  // guaranteed to be receive events in order
		print(x as string);
	}
}
```

## Concurrency

While according to the specification of penguin-lang, the compiler can implement everything into one thread. However, because one design goal of penguin-lang is to be as concurrent as possible, so it's recommanded that the compiler by default create a thread-pool with size equal to cpu cores, and if no data race is detected, automatically utilizes work loads onto these threads. Consider following code:

```
var a = 0;

initial {
    for (i in 0..5) {
        a++;
	}
}

initial {
    for (i in 0..5) {
        a++;
	}
}
```

If two routines mutablely visit one variable, the compiler **MUST** ensure there are no data race. In this case, the compiler will automatically apply a RW lock to 'a'.

If you want them to be parallel, you can change definition of `a` to `var a: Atomic<i32> = 0`, so the compiler can safely dispatch the two routines to different threads.
This is because class `Atomic<?>` and `MpscQueue<?>` used below implements `ISynchronized`.


## Folking
You can use folk keyword to generate parallel initial blocks.
```
initial folk(5) {
	log.info("hello!");
}
```
Above code will print 5 'hello' as expected. You can also use `initial folk():` to automatically get best parallel performance.


## Event & Queues
Events can also be dispatcher to different handlers running in multiple threads. This is because event parameters are read-only, so multiple handlers will not cause data race. However, events are not good at collecting results, so we can use a queue. A typicial multiple producers multiple consumers program may look like:
```
event new_job: Job;
MpscQueue<Result> result_queue;

initial folk() {
	job = create_job();
	emit new_job(job);
}
	
initial {
	wait result_queue.size == 10;
	print("all done!");
	exit();
}
	
on new_job(job) {
	val result = process(job);
	result_queue.enqueue(result);
}

```
Above code will automatically dispatch jobs according to max concurrency allowed, because compiler can infer that different `on new_job` handler mutates an thread-safe object `result_queue`, so it's considered as a 'pure' function.

## Scalability
The penguin-lang by natural allows scalable concurrency, from one single thread to multi-threads, multi-processes, or even multi-hosts. It's ok to design a scheduler that run on multiple hosts, and the can automatically distribute the workloads to different hosts.
