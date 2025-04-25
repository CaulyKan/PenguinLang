## IO Asynchronization

The penguin-lang is by default processing IO asynchronously. That means it will not waiting for I/O to complete, but try to run as much code at current thread as possible. For example:

```
import std.file;
fun my_read_all() -> string {
    val text = file.read_all("1.txt");
    println("read length: " + text.length());
}

initial {
    val text = my_read_all("1.txt");
    val a = 1 + 1;
    println("a={}", a);
}
```

Above code will print:

```
a=2
read length: 123
```

Please note that `a=2` is earlier than `read length`, because compiler find that the calculation and printing of `a` is not blocked by the result of `my_read_all`, so compiler will do the calculation, while let the file reading run in background. If the order is mattering, you can use `wait` keyword, for example:

```
    val text = wait file.read_all("1.txt");  
    val text2 = file.read_all("2.txt");
    val text3 = file.read_all("3.txt");
    wait text2 and text3;
```

## Event Asynchronization

Similar to IO, event is by default run asynchronously. Consider following code:
```
event A(x);
event B(x);

initial {
	for (i in 0..10) {
		raise A(x);
		raise B(x);
		raise C(x);
	}
}

on A(x) {
	println("A: {}",x);
}
	
on !pure (B(x)) {
	println("B: {}", x);
}
	
var c = 0;
on C(x) {
	c = x;
	print("C: {}", x);
}
```
Possible printing can be:
```
A: 0
B: 0
B: 1
A: 2
A: 1
A: 4
C: 0
B: 2
C: 1
C: 2
```
As you may notice, the order of A event printing is UNCERTAIN, while order B & C event printing is certain. If penguin-lang compiler see no dependency between event handlers, or even single event handler itself, the compiler consider these event handlers can run asynchronously, then penguin-lang runtime is free to re-order or parallelize these event handlers.
If this is not what you expected, you can mark the event handler as `!pure`, which means the handler has side effect and is not a 'pure function', which force compiler to run it synchronously. These will also happen when the handler writes to a mutable variable without any protection, or calls a non-pure function such as writing a file, like `C` in above code.

Another way to achieve ordering is wait the event on the emitter side. 
```
event A, B;

initial {
    wait raise A;
    raise B;
}
    
on (A) {
    print("A");
}

on (B) {
    print("B");
}
```

Above code will print `AB` in certern order. basically `raise A` is an event-expression which generate an anonymous event, which happens when 'every handler of A has completed', and is waitable.

## Concurrency

While according to the specification of penguin-lang, the compiler can implement everything into one thread. However, because one design goal of penguin-lang is to be as concurrent as possible, so it's recommanded that the compiler by default create a thread-pool with size equal to cpu cores, and if no data race is detected, automatically utilizes work loads onto these threads. Consider following code:

```
var a = 0;

initial {
    for (i in 0..5) {
        a++;
        log.info("Add from block 1");
	}
}

initial {
    for (i in 0..5) {
        a++;
        log.info("Add from block 2");
	}
}
```

If two routines mutablely visit one variable, the compiler **MUST** implement them in order. So above code may print something like:
```
INFO 0000:00:01 [thread1] Add from block 1 
INFO 0000:00:02 [thread1] Add from block 1 
INFO 0000:00:03 [thread1] Add from block 1 
INFO 0000:00:04 [thread1] Add from block 1 
INFO 0000:00:05 [thread1] Add from block 1
INFO 0000:00:06 [thread1] Add from block 2 
INFO 0000:00:07 [thread1] Add from block 2 
INFO 0000:00:08 [thread1] Add from block 2
INFO 0000:00:09 [thread1] Add from block 2
INFO 0000:00:10 [thread1] Add from block 2 
```

If you want them to be parallel, you can change definition of `a` to `var a: atomic<int> = 0`, so the compiler can safely dispatch the two routines to different threads.

Another tools you can use are lock and mutex. Lock example:

```
var a = 0;

fun my_incr() {
	locked {
        a++;
	}
}

initial {
    for (i in 0..5) {
        my_incr();
	}
}

initial {
    for (i in 0..5) {
        my_incr();
	}
}
```

Mutex example:

```
import std.mutex
var a: mutex<int> = 0
var b: mutex<int> = 0

initial:
    for i in 0..5:
        locked a,b:
            a+=b
			b++

initial:
    for i in 0..5:
        locked a:
            a++
```

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
event new_job(Job);
mpsc_queue<Result> result_queue;

initial folk() {
	job = create_job();
	raise new_job(job);
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
Above code will automatically dispatch jobs according to max concurrency allowed, because compiler can infer that different `on dispatch_queue` handler all mutate an thread-safe object `result_queue`.

## Scalability
The penguin-lang by natural allows scalable concurrency, from one single thread to multi-threads, multi-processes, or even multi-hosts. Penguin-lang implements this by making synchronization mechanism a trait, which is open for public implementation. Lets re-implement last example and see how it scales!
```
event new_job(Job);
mpsc_queue<Result> result_queue;

class single_thread_queue<T> {
	var q: queue<T>;
	impl event_queue<T> {
		// ... implement a event_queue with a normal queue which is not thread-safe and better performance
	}
}

class kafka_queue<T> {
	impl event_queue<T> {
		// ... implement a event_queue with kafka which allows multiple process to communicate
	}
}

initial:
	if (sys.args[1] == "single-thread")
		result_queue = new single_thread_queue<Result>;
	else if (sys.args[1] == "multi-threads")
		// event_queue is default for multi-threads use, so do nothing
	else if (sys.args[1] == "multi-process")
		result_queue = new kafka_queue<Result>;
	
	for (_ in folk()) {  // folk will automatically dispatch jobs across each possible cores
		start()
	}
		
initial {
	wait result_queue.size() == 10;
	print("all done!");
	exit();
}

fun start() {
	job = create_job();
	raise new_job(job);
}
	
on new_job(job) {      // event handler will also automatically process events across each possible cores
	val result = process(job);
	result_queue.enqueue(result);
	print("job {}", job.id);
}
```

you can run above code with different concurrent level (note that params starts with `++` goes to penguin-lang runtime):
```
> ./test single-thread ++max_concurrency=1thread
INFO 0000:00:01 [thread1] job 1 
INFO 0000:00:01 [thread1] job 2 
INFO 0000:00:01 [thread1] job 3 
...

> ./test multi-thread ++max_concurrency=8threads
INFO 0000:00:01 [thread1] job 1 
INFO 0000:00:01 [thread2] job 2 
INFO 0000:00:01 [thread3] job 3 
...

> ./test multi-process ++max_concurrency=8processes,8threads ++scheduler_type=master ++scheduler_socket=127.0.0.1:8000
INFO 0000:00:01 [thread1] job 1
INFO 0000:00:01 [thread2] job 5
...

> ./test ++max_concurrency=8threads ++scheduler_type=slave ++scheduler_socket=127.0.0.1:8000
INFO 0000:00:01 [thread1] job 2
INFO 0000:00:01 [thread2] job 3
...

> ./test ++max_concurrency=8threads ++scheduler_type=slave ++scheduler_socket=127.0.0.1:8000
INFO 0000:00:01 [thread1] job 4 
INFO 0000:00:01 [thread2] job 6
...
```
Penguin-lang do not enforce any scheduler/runtime implementation, so above example may not work for all penguin-lang compilers. However the idea is, Penguin-lang schedules tasks on different concurrent models atomatically, with as few intrusions to business code as possible.