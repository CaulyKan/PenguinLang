Execution Flow
----------------------
Differs from most programming languages, penguin-lang starts with `initial` not `main`. The interesting part is that there can be multiple `initial` parts, executing together in same time. So, given following code:
```
initial {
	print("A");
}
	
initial {
	print("B");
}
	
initial {
	print("C");
}
```
The result of above code is uncertain. It may prints `ABC`, or `CBA`, or any possible combinations. These routines also are not guranteed to run in one thread or multiple threads. Yes, penguin-lang takes control of threading away from programmer, in most circumstances.

Events
---------
In order to run `initial` blocks in sequence, you can use event, which is a core concept in penguin-lang. Following is a very basic Example of using event:
```
event a_finished;  // define an event
initial {
	print("A");
	emit a_finished; 
}
	
initial {
	wait a_finished;
	print("B");
}
```

Since `wait` keyword will block execution flow until event happen,  Above code will print `AB` definitely. 

Penguin-lang also provide `on` control block, which is similar to 'callbacks' in most other languages.
```
event foo;

initial {
	emit foo;
}
	
on foo {
	print("A");
}
	
on foo {
	print("B");
}
```

In above code, when event `foo` happens, the `on foo` blocks will be executed, however the order of execution is uncertain, so the result is still uncertain between `AB` or `BA`. 

Event expression
----------------
There are some expression that can produce an event. For example:
```
var a = 0;

initial {
	a = 1;
	a = 10;
	a = 5;
}

on a == 10 {
	print("a is {}", a);
}
```
Above code will print `a is 10`, because the `on` block will only happen when `a` 'changes to' 10. The `a == 10` will generate an annomous event, which will be triggered when `a` is changed.

Events with data
------------------
Events can take parameters, for example:
```
event foo : i32;

initial {
	emit foo(1);
	emit foo(2);
}

on foo(val x: i32) {
	println("foo with x={}", x);
}
```
The result will be:
```
foo with x=1
foo with x=2
```

It's also possible to use `wait` keyword with event expression which return a value of event type, for example:
```
event foo : i32;

initial {
	emit foo(1);
}

initial {
	val x : i32 = wait foo;
	println(x as string);
}
```

Waiting for events
------------------
Penguin-lang provide `wait` keyword to wait for an event to happen. It's possible to use `wait` keyword with event expression, for example:
```
event foo : i32;
initial {
	emit foo(1);
}

initial {
	val x : i32 = wait foo;
	println(x as string);
}
```

In above code, the `wait foo` will block execution flow until `foo` event happens, and then the value of `x` will be assigned to `1`.

There's an important difference between `wait` event and `on` routine. The `wait` event will only receive the next event after the time it started waiting, which means it's possible to miss some events. On the other hand, `on` routine will receive all events in order.

```
event foo : i32;
initial {
	for (var i : i32 in range(0,10))
		emit foo(1);
}

initial {
	while (true) {
		val x = wait foo;	// may miss some events
	}
}

on foo(val x: i32) {
	// will receive all events
}
```