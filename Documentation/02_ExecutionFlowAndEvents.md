Execution Flow
----------------------
Unlike most programming languages, penguin-lang starts with `initial` instead of `main`. There can be multiple `initial` parts, which may execute concurrently. For example:
```
initial {
	print("A");
}

initial {
	print("B");
}
```
The result of the above code is uncertain, because these routines are not guaranteed to run in one thread or multiple threads.

Penguin-lang is designed to take control of threading away from the programmer, while ensuring multi-threading safety automatically.

Events
---------
Penguin-lang provides a builtin event system. You can use events to control execution order:
```
event a_finished;
initial {
	print("A");
	sleep(1);
	emit a_finished;
}

initial {
	wait a_finished;
	print("B");
}
```

The `wait` keyword will block execution flow until the event happens. The above code will always print `A` then `B`.

You can also use `on` blocks, which are similar to callbacks:
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
The result of the above code is uncertain, because the two routines can be parallelized.

Event expressions
----------------
You can use expressions as events:
```
var a : i32 = 0;

initial {
	for (var i : i32 in range(0, 10)) {
		a = i;
	}
}

on a == 5 {
	println("a is 5");
}
```

Events with data
------------------
Events can take parameters:
```
event foo : i32;

initial {
	for (var i : i32 in range(0, 10))
		emit foo(i);
}

on foo(const i : i32) {
	print(i as string);
}
```

Waiting for events
------------------
The `wait` keyword can be used with events that have data:
```
event foo : i32;
initial {
	emit foo(1);
}

initial {
	var x : i32 = wait foo;
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
