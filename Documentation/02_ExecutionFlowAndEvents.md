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

Control Flow
---------
### if / else
`if` works as a statement and as an expression — in expression position the value is the last expression of the executed branch:
```
if (x > 0) { print("positive"); } else if (x == 0) { print("zero"); } else { print("negative"); }

let y : i32 = if (x == 1) { 2 } else { 3 };
```

### while
`while` also works as an expression; its value comes from `break <expr>;`:
```
while (i < 10) { i += 1; }

let found : i64 = while (true) {
	if (at_end()) { break -1; }
	step();
};
```

### for
`for` is for-in only (there is no C-style `for(;;)`). The loop variable may carry a type; `mut` on `let` selects the mutable iterator path:
```
for (let i : i64 in range(0, 3)) {
	print(cast<string>(i));
}

for (let item in list) { ... }        // uses iter()
for (let mut item in list) { ... }    // uses iter_mut()
```
The iterable desugars to `iter()`/`iter_mut()` over `IIterator<T>.next() -> Option<T>`; an expression that is already an iterator is used as-is. `let mut` cannot be combined with an explicit type — `for (let mut i : i64 in ...)` is a compile error.

### try-bind
`if (let x := expr)` binds the payload of an optional value and runs the body only when it is readable:
```
let a = new Option<i32>.some(42);
if (let v := a.some) {
	print(cast<string>(v));    // runs only when a holds a payload
} else {
	print("none");
}
```
An optional type annotation is allowed: `if (let v : i32 := a.some)`.

### try / catch
`panic` throws a `RuntimeError`; `try`/`catch` catches it:
```
try {
	panic("boom");
} catch (e) {
	print(e.message);    // RuntimeError has .message and .code
}
```

There is no `match`/`case` statement — combine `is` checks with `if`/`else` chains.

Events
---------
Penguin-lang provides a builtin event system. An event is a first-class value (`Event<T>`): store it anywhere, pass it to functions, emit from anywhere. You can use events to control execution order:
```
let a_finished : mut Event<void> = new Event<void>();
initial {
	print("A");
	wait;
	a_finished.emit(void);
}

initial {
	wait a_finished;
	print("B");
}
```

The `wait` keyword will block execution flow until the next emission. The above code will always print `A` then `B`.

Subscription is a wait loop:
```
let foo : mut Event<void> = new Event<void>();

initial {
	foo.emit(void);
}

initial {
	while (true) {
		wait foo;
		print("A");
	}
}

initial {
	while (true) {
		wait foo;
		print("B");
	}
}
```
Every routine parked on the event receives the value — broadcast, one delivery per parked `wait` per emit.

Waiting for conditions
----------------
`wait` also accepts a plain condition (level-sensitive wait). The routine parks and the condition is re-checked every scheduler round until it holds:
```
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

Events with data
------------------
Events carry a payload type:
```
let foo : mut Event<i32> = new Event<i32>();

initial {
	for (let i : i32 in range(0, 10))
		foo.emit(i);
}

initial {
	while (true) {
		let i : i32 = wait foo;
		print(cast<string>(i));
	}
}
```

Waiting for events
------------------
The `wait` keyword on a payload event returns the delivered value:
```
let foo : mut Event<i32> = new Event<i32>();
initial {
	foo.emit(1);
}

initial {
	let x : i32 = wait foo;
	println(cast<string>(x));
}
```

In above code, the `wait foo` will block execution flow until `foo` event happens, and then the value of `x` will be assigned to `1`.

Note that a `wait` only receives emissions from the moment it parks: a value emitted while nobody is waiting is lost (broadcast, not queueing). A wait loop therefore sees every value only if it re-parks between emissions — which `emit` guarantees by yielding one delta after every broadcast. When every emission must be preserved regardless of consumer pacing, use a `Fifo` channel instead (see `11_PortsChannelsEvents.md`).
