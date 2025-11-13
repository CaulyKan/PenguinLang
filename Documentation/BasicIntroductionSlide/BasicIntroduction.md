[comment]: # 'Build the presentation: mdslides ./basic-introduction.md) --include="Y:\Workspace\penguinlang\Documentation\slides1-basic-introduction'
[comment]: # (THEME = night)
[comment]: # (CODE_THEME = dark)


Cauly Kan | May, 2025

## A Basic Introduction to PenguinLang

[comment]: # (!!!)

### Motivation
Build a modern, easy-to-understand, and concurrent-friendly programming language.

```
initial {
	println("hello world from penguin-lang!");
}
```

[comment]: # (!!!)

### Roadmap
* **BabyPenguin**: A C# implementation of PenguinLang compiler & virtual machine 
  * Emits custom BabyPenguinIR 
  * Currently usable with limited features of penguin-lang
  * Used for self-bootstrapping the **EmperorPenguin**

[comment]: # (|||)

* **MagellanicPenguin**: LSP/DAP implementation for PenguinLang
  * Supports VSCode 
  * Use **BabyPenguin** as backend
  * Currently usable with limited features

[comment]: # (|||)

* **EmperorPenguin**: A full implementation of PenguinLang compiler using **BabyPenguin**
  * Emits llvm IR
  * Not started yet

[comment]: # (!!!)

The easiest way to get started with PenguinLang is to install `PenguinLang` extension in VSCode.

```
initial {
	println("hello world from penguin-lang!");
}
```

- Save above code as `hello.penguin`
- Press F5, add a new default 'PenguinLang Debug' profile
- Press F5 again to start running! 

[comment]: # (!!!)

### Type System

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

[comment]: # (|||)

All PenguinLang types are either value types or reference types.

<div style="font-size: 0.8em;">

| Type            | Who                                                            | Managed By                     | Assignment       |
| --------------- | -------------------------------------------------------------- | ------------------------------ | ---------------- |
| Value types     | i32, f64, string...<br /> classes that implements `IValueType` | Stack or Parent Data Structure | Always copied    |
| Reference types | any other types                                                | GC                             | Shared reference |

</div>

[comment]: # (|||)

Mutability: use `let`/`mut` to declare variables.

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

[comment]: # (|||)

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

[comment]: # (!!!)

### Execution Flow

PenguinLang starts with `initial` not `main`. The interesting part is that there can be multiple `initial` parts.

```
initial {
	print("A");
}
	
initial {
	print("B");
}
```

<div style="font-size: 0.8em;">

- The result of above code is uncertain, because these routines are not guranteed to run in one thread or multiple threads. 
  
</div>

[comment]: # (|||)

One important idea is, PenguinLang is designed to takes control of threading away from programmer, while ensuring
multi-threading safety automatically.

[comment]: # (|||)

PenguinLang ships a builtin Event system, which is a very useful for auto parallelization:

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

<div style="font-size: 0.8em;">

- Again, the result of above code is uncertain, because the two routines can be parallelized.
  
</div>

[comment]: # (|||)

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

<div style="font-size: 0.8em;">

- Again, the result of above code is uncertain.
- There are some ways to avoid auto parallelization, basically make the event handler non-pure.
  
</div>

[comment]: # (|||)

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

[comment]: # (|||)

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

[comment]: # (!!!)

## Functions

Function itself in PenguinLang is nothing special. But it's important that PenguinLang adopts full stackless coroutine model, and will atomatically identify a function is 'async' or not.

```
fun foo() -> i32 { return 1; }    	// normal non-async function
fun bar() { wait; }					// use of wait make it an async function
fun baz() { bar(); }				// call another async function, making itself async too
```

[comment]: # (|||)

Spawn a new function asynchronosly:
```
fun foo() -> i32 { return 1; }
fun bar() {
	let task : mut IFuture<i32> = async foo();		// foo can be async or not
	let y : i32 = wait task;
}
```

<div style="font-size: 0.8em;">

- In fact, direct calling of an async function (e.g. `bar()`) is a shorthand for `wait async bar();`

</div>

[comment]: # (|||)

PenguinLang can't prevent data-racing between coroutines, but it can parrallelize workload without introducing additional data-racing

<div style="font-size: 0.8em;">

- Consider following code:

</div>

```
let foo : mut Box<i32> = new Box(0);		// Box<T> warps a value type to reference type
										// PenguinLang forbids global value type variables
fun bar() {
	let i : mut i32 = foo.get();			// get() is async function
	i += 1;
	foo.set(i);							// set() is also async function	
}
```

[comment]: # (|||)

PenguinLang can shedule a job on another thread when following rules are met:

<div style="font-size: 0.8em;">

- Initially, every routines that access one global variable must run on one same thread. 
	- Combined with following, one global variable is always accessed on same thread.
- A new spawned routine is safe to schedule on another thread ONLY IF:
	- It captures only value types, or 
	- It captures reference types must all implements `ISynchronizable` interface.
- Otherwise, the new spawned routine must run on the same thread as the caller.

</div>

[comment]: # (|||)

<div style="font-size: 0.8em;">

- Back to previous example, `bar` function has two async calls: `get` and `set`, (so it has two `wait`)
- The compiler will split `bar` function into three sub-routines:

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

</div>

[comment]: # (|||)

A smarter way is to make use of `ISynchronizable` interface and predefined synchronization types, 
such as `Atmoic`, `ConcurrentQueue`, `Mutex`, etc.

Another advantage of PenguinLang is that it can run GC on local threads, because it knows that reference type
objects will not leak to other threads. This helps avoid 'Stop The World' problem.


[comment]: # (!!!)

### Class And Interface

Use Class to form a data structure

```
class Person {
	name: string;
	age: !mut i32;   // immutable after construction
}
```

<div style="font-size: 0.8em;">

- If all member of a class is Value Type, then the class itself is also a Value Type. (Automatically Implements `IValueType`)
- Otherwise, the class is a Reference Type. (Automatically Implements `IReferenceType`)
- You can change the default behavior by explicity implementing `IValueType` or `IReferenceType` interfaces.
  
</div>

[comment]: # (|||)

Similar to Rust, function in class must have `this` as first parameter to be a 'method', otherwise it's a 
'static' function in other languages.

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

[comment]: # (|||)

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

[comment]: # (|||)

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

[comment]: # (|||)

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

[comment]: # (!!!)

### Enums 

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

[comment]: # (!!!)

## Thank you!

[https://github.com/CaulyKan/PenguinLang](https://github.com/CaulyKan/PenguinLang) 