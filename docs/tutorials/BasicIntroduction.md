# A Basic Introduction to PenguinLang

Penguin language (or 'penguin-lang') is designed to be a programming language that is easy-to-use, easy-to-understand, and concurrent-friendly.

## Motivation

Build a modern, easy-to-understand, and concurrent-friendly programming language.

```
initial {
	println("hello world from penguin-lang!");
}
```

## Roadmap

* **BabyPenguin**: A C# implementation of PenguinLang compiler & virtual machine
  * Emits custom BabyPenguinIR
  * Currently usable with limited features of penguin-lang
  * Used for self-bootstrapping the **EmperorPenguin**
* **MagellanicPenguin**: LSP/DAP implementation for PenguinLang
  * Supports VSCode
  * Use **BabyPenguin** as backend
  * Currently usable with limited features
* **EmperorPenguin**: A full implementation of PenguinLang compiler written in PenguinLang itself, built with **BabyPenguin**
  * Emits LLVM IR and native executables
  * Self-hosting: compiles its own sources

Penguin-lang is inspired from multiple languages: syntax from C, garbage collection from C#/Java, type system from Rust, asynchronization/coroutines from Golang, and concurrency from Verilog/SystemC.

## Getting Started

The easiest way to get started with PenguinLang is to install the `PenguinLang` extension in VSCode.

```
initial {
	println("hello world from penguin-lang!");
}
```

- Save above code as `hello.penguin`
- Press F5, add a new default 'PenguinLang Debug' profile
- Press F5 again to start running!

## Type System

- Statically typed
- Value types and Reference types
- Type has mutability: `let` and `mut`
- has a `void` type
- no `null`/`none` value, use `Option<T>` instead
- `as` to perform type casting

```
let x: i32 = 1;
let mut y: string = "hello";
```

All PenguinLang types are either value types or reference types.

| Type            | Who                                                            | Managed By                     | Assignment       |
| --------------- | -------------------------------------------------------------- | ------------------------------ | ---------------- |
| Value types     | i32, f64, string... classes that implements `IValueType`       | Stack or Parent Data Structure | Always copied    |
| Reference types | any other types                                                | GC                             | Shared reference |

### Mutability

Use `let`/`mut` to declare variables.

```
let x: i32 = 1;
x = 2; // ERROR

let mut y: i32 = 1;
y = 2; // OK

let z : !mut i32 = 1;
z = 2; // ERROR, identical to above

let foo: !mut List<i32>;   // can't add/remove elements, nor modify elements
let bar: List<!mut i32>;   // can add/remove elements, but can't modify elements
```

More on mutability:

- Value types are free to be assigned regardless of mutability -- they are always copied
- Reference types:
  - immutable to mutable: not allowed unless in initialization.
  - mutable to immutable: implicitly allowed

```
let a: MyClass = new MyClass();
let b: mut MyClass = a; // OK
b = a; // Compile-time ERROR

let mut x: MyClass = new MyClass();
let y: MyClass;
y = x; // Compile-time ERROR
```

## Execution Flow

PenguinLang starts with `initial` not `main`. The interesting part is that there can be multiple `initial` parts.

```
initial {
	print("A");
}

initial {
	print("B");
}
```

- The result of above code is uncertain, because these routines are not guaranteed to run in one thread or multiple threads.

One important idea is, PenguinLang is designed to take control of threading away from programmer, while ensuring multi-threading safety automatically.

## Events

PenguinLang ships a builtin Event system, which is very useful for auto parallelization:

```
event foo;

initial { emit foo; }

on foo {
    print("A");
}

on foo {
    print("B");
}
```

- Again, the result of above code is uncertain, because the two routines can be parallelized.

Even single event handler can be parallelized:

```
event foo : i32;

initial {
	for (let mut i : i32 in range(0, 10))
		emit foo(i);
}

on foo(i : i32) {
	print(i as string);
}
```

- Again, the result of above code is uncertain.
- There are some ways to avoid auto parallelization, basically make the event handler non-pure.

Use `wait` to wait for an event:

```
event a_finished;  		// define an event
initial {
	print("A");
	sleep(1); 			// avoid running too fast
	emit a_finished;
}

initial {
	wait a_finished;	// wait for event
	print("B");
}
```

Using expression with `on` routines:

```
let mut a : i32 = 0;

initial {
	for (let i : i32 in range(0, 10)) {
		a = i;
	}
}

on a == 5 {
	println("a is 5");
}
```

## Functions

Function itself in PenguinLang is nothing special. But it's important that PenguinLang adopts full stackless coroutine model, and will automatically identify a function is 'async' or not.

```
fun foo() -> i32 { return 1; }    	// normal non-async function
fun bar() { wait; }					// use of wait make it an async function
fun baz() { bar(); }				// call another async function, making itself async too
```

Spawn a new function asynchronously:

```
fun foo() -> i32 { return 1; }
fun bar() {
	let task : mut IFuture<i32> = async foo();		// foo can be async or not
	let y : i32 = wait task;
}
```

- In fact, direct calling of an async function (e.g. `bar()`) is a shorthand for `wait async bar();`

### Thread Scheduling

PenguinLang can't prevent data-racing between coroutines, but it can parallelize workload without introducing additional data-racing. Consider following code:

```
let foo : mut Box<i32> = new Box(0);	// Box<T> wraps a value type to reference type
										// PenguinLang forbids global value type variables
fun bar() {
	let i : mut i32 = foo.get();		// get() is async function
	i += 1;
	foo.set(i);							// set() is also async function
}
```

PenguinLang can schedule a job on another thread when following rules are met:

- Initially, every routines that access one global variable must run on one same thread.
  - Combined with following, one global variable is always accessed on same thread.
- A new spawned routine is safe to schedule on another thread ONLY IF:
  - It captures only value types, or
  - It captures reference types must all implements `ISynchronizable` interface.
- Otherwise, the new spawned routine must run on the same thread as the caller.

Back to previous example, `bar` function has two async calls: `get` and `set`, (so it has two `wait`). The compiler will split `bar` function into three sub-routines:

```
class bar {
	i : mut i32;
	foo : mut Box<i32>;
	fun step1(mut this: bar) {
		this.i = this.foo.get();
	}
	fun step2(mut this: bar) {
		this.i += 1;
	}
	fun step3(mut this: bar) {
		this.foo.set(this.i);
	}
}
```

- `step2` captures only value types `this.i`, so it can be safely run on another thread.
- PenguinLang will always perform `step1` and `step3` on same thread, which ensures thread-safety.

A smarter way is to make use of `ISynchronizable` interface and predefined synchronization types, such as `Atomic`, `ConcurrentQueue`, `Mutex`, etc.

Another advantage of PenguinLang is that it can run GC on local threads, because it knows that reference type objects will not leak to other threads. This helps avoid 'Stop The World' problem.

## Class And Interface

Use Class to form a data structure:

```
class Person {
	name: string;
	age: !mut i32;   // immutable after construction
}
```

- If all member of a class is Value Type, then the class itself is also a Value Type. (Automatically Implements `IValueType`)
- Otherwise, the class is a Reference Type. (Automatically Implements `IReferenceType`)
- You can change the default behavior by explicitly implementing `IValueType` or `IReferenceType` interfaces.

Similar to Rust, function in class must have `this` as first parameter to be a 'method', otherwise it's a 'static' function in other languages.

```
class Person {
	x: i32;
	fun set(mut this) {
		this.x = 1;
	}
	fun get(this) -> i32 {
		return this.x;
	}
}
```

PenguinLang also has interface similar to Rust's trait, which can have default implementation.

```
interface IHello {
	fun hello(this: IHello) {
		println("hello");
	}
}

class Hello {
	impl IHello;
}
```

Override default implementation in class:

```
class Hi {
	let name: mut string;
	impl IHello {
		fun hello(this: IHello) {
			IHello.hello(this); 		// can call default implementation

			let self = this as Hi; 	// must cast to `Hi` to access `name` field
			println("hi" + self.name);
		}
	}
}
```

Extending class from outside is allowed:

```
#template(T: type)
class Foo { }

impl IHello for Foo<i32> {
	fun hello(this: IHello) {
		println("hello from i32");
	}
}
```

## Enums

PenguinLang use Rust style enum, which can contain value.

```
#template(T: type)
enum Option {
	some: T,
	None,
}

initial {
	let x : mut Option<i32> = new Option<i32>.some(1);
	if (x is Option<i32>.some) {
		println(x.some as string);
	}
}
```

## Meta Programming

Penguin-lang provides compile-time meta-programming through **Meta Functions** (`#fun`) and **Compile-Time Evaluation** (`const if`, `const for`).

```
#fun fib(n: u32) -> u32 {
    if (n <= 1) return n;
    return fib(n-1) + fib(n-2);  // recursive, no # prefix needed
}

initial {
    let x = #fib(10);  // x = 55, computed at compile time
}
```

`const if` enables conditional code generation at compile time:

```
#template(T: type)
fun default_value() -> T {
    const if (T == i32) {
        return 0;
    } else if (T == f32) {
        return 0.0;
    } else {
        return T.default();
    }
}
```

- All `else if` / `else` branches automatically inherit `const` property
- Condition must be evaluable at compile time

`const for` enables loop unrolling at compile time:

```
#template(N: u32)
fun sum() -> u32 {
    let result: u32 = 0;
    const for (i in 0..N) {
        result = result + i;
    }
    return result;
}
```

- Loop is fully unrolled during compilation
- Zero runtime overhead

`#template` is syntactic sugar for a meta function that returns a type:

```
// These are equivalent:

#template(T: type)
class Box<T> { value: T; }

// ...is equivalent to:

#fun Box(T: type) -> type {
    return class { value: T; };
}
```

- **BabyPenguin**: Supports `const if/for`, `#template`
- **EmperorPenguin**: Full `#fun` support, type reflection

See the [Meta Programming tutorial](./MetaProgrammingIntroduction.md) for a full walkthrough of EmperorPenguin's meta programming features.

## Links

- GitHub: [https://github.com/CaulyKan/PenguinLang](https://github.com/CaulyKan/PenguinLang)
