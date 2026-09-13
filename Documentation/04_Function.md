## Function
The following code defines a function with no parameters and no return value.
```
fun hello() {
	println("hello");
}
```

### Defining Parameters
```
class MyClass {
	foo: i32 = 0;
	impl IReferenceType;   // reference type: assignments share the object
}

fun hello(param1 : i32, param2 : mut i32, param3 : MyClass, param4 : mut MyClass) {
	// param1 = 0;		// ERROR: can't change an immutable param

	param2 = 1;  	// can change value of param2, 
					// but will not affect caller because i32 is copied
					
	// param3 = new MyClass(); // ERROR: can't change an immutable param
	// param3.foo = 1;			// ERROR: can't mutate an immutable param
	
	param4.foo = 1;			// OK, caller object is mutated (shared reference)
	param4 = new MyClass(); // OK, but only rebinds the local parameter;
	                        // the caller's object is NOT changed
}
```
For a value-type parameter, `mut` permits mutating the local copy only — writes never escape the callee. Only the method receiver (`mut this`) aliases the caller's slot.

### Return Values
```
fun foo() -> i32 {
	return 1;
}

initial {
	let a: i32 = foo();
}
```

Return value also has mutability. By default, return value is immutable. To make it mutable, use `mut` keyword.
```
fun clone(foo: Foo) -> mut Foo {
	return new Foo(foo.x, foo.y);
}

fun borrow(foo: Foo) -> Foo {
	return foo;
}
```

### Generator Function
PenguinLang supports generator functions, which can be paused and resumed. They use the `yield` keyword to return a value and pause execution.

```penguin
fun test() -> mut IGenerator<i32> {
	yield 1;
	yield 2;
	yield 3;
}

initial {
	for (let v : i32 in test()) {
		println(cast<string>(v));
	}
}
```

A generator function can also have a final `return` statement.
```penguin
fun test() -> mut IGenerator<i64> {
    yield 1;
    yield 2;
    return 3;
}
```

### Simple Function And Stateful Function
Function can be simple or stateful. 
If 'wait' is used in function, or the function calls a stateful function, the function is a stateful function.
If a function is a generator function, it is a stateful function.
We will cover this topic in asynchonous chapter.

### Iterator Combinators
Every iterator (the concrete `RangeIterator` / `MapIterator` /
`FilterIterator` classes, and `std.Vector`'s `_VectorIterator`) carries the
combinator methods `map` / `filter` / `reduce` / `all` / `any` / `into`.
Chaining works because `range()` and `iter()` return the CONCRETE iterator
type — combinators on an `IIterator`-typed value are not dispatchable (the
vtable slot set of a generic method is not knowable per call site), so keep
the chain on the concrete type:

```penguin
initial {
    let pull : mut Option<i64> = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; })
        .filter(fun (x: i64) -> bool { return x > 1; }).next();   // some(2)
    let total : i64 = range(0, 4)
        .map(fun (x: i64) -> i64 { return x + 1; })
        .reduce(0, fun (acc: i64, x: i64) -> i64 { return acc + x; }); // 1+2+3+4 = 10
    println(cast<string>(pull.some) + " " + cast<string>(total));
}
```

*   `map<U>(f)` / `filter(pred)` are LAZY — the source is only pulled when the
    resulting iterator's `next()` is called.
*   `map` infers `U` from the lambda's return type; `reduce<R>(init, f)`
    infers `R` from `init`; no explicit type arguments needed.
*   `into<C>()` materializes the iterator into any container with a default
    constructor and a `push` method — `C` is an EXPLICIT type argument:
    `v.iter().map(f).into<std.Vector<i64>>()`.
*   `for (let x : i64 in range(0, 3).map(f))` works too — for-in accepts any
    iterator-typed operand as-is.

### Function in Class (Methods)
Classes can define functions, which are also called methods.

*   **Instance Methods**: If the first parameter of a method is `this`, it's an instance method and can access the object's data. The `this` parameter must have its mutability specified (`this` or `mut this`).
*   **Static Methods**: If the first parameter is not `this`, the method is a static method. It cannot access the object's data and can be called directly on the class itself.

```penguin
class Foo {
	name: string = "Foo";
	fun hello_world() { // Static method
		println("hello");
	}

	fun hello_myself(this) { // Instance method
		println("hello " + this.name);
	}
}

initial {
    let mut foo = new Foo();
    foo.hello_myself();		// OK, 'foo' instance is passed as 'this' parameter
    foo.hello_world();		// OK, can be called on an instance
    Foo.hello_world();		// OK, can be called directly on the class
}
```

### Constructor
Function 'new' is used as constructor in class. The first parameter must be mutable 'this', which is the instance of the class being created. If no 'new' function is defined, a default one is created.
```
class Foo {
	x: u8 = 1;
	fun new(mut this, x : u8) {
		this.x = x;
	}
}

initial {
	let foo = new Foo(2);
}
```

Immutable class members can only be initialized in the constructor or variable declaration.

### Lambda Function
Lambda function is a function that is defined inline in a block of code.
```
fun foo() {
	let f : fun<i32, i32> = fun(x: i32) -> i32 {
		return x * 2;
	};
	println(cast<string>(f(3)));
}
```


### Function Values
`fun<R, P1, P2, ...>` is a first-class function value type. The FIRST type
argument is the RETURN type, the rest are the parameter types
(`fun<i32, i32>` = takes one `i32`, returns `i32`; `fun<void>` = no params,
returns nothing). `async_fun<R, P...>` is the suspending variant; `fun` and
`async_fun` with equal signatures are interchangeable (calling either runs it
inline on the current coroutine stack — the callee's `wait` suspends the
caller). Function types nest inside generics (`Option<fun<void>>`,
`List<fun<i32, i32>>`).

Four sources of function values:

1. **Function reference** — `let f : fun<i32, i32> = twice;`
2. **Bound method reference** — `let g : fun<i32, i32> = x.call;` — the
   receiver is fixed at reference time: `g(2)` ≡ `x.call(2)`
3. **Unbound method reference** — `let h : fun<i32, Temp, i32> = ns.Temp.call;`
   — the receiver stays the FIRST PARAMETER of the function type: `h(x, 2)`
   ≡ `x.call(2)`. This completes the value form of the call sugar
   `x.call(2) ≡ ns.Temp.call(x, 2)`. Interface methods (`I.b`) have no unique
   implementation and cannot be referenced unbound.
4. **Lambda** — `fun(x : i32) -> i32 { return x + 1; }` (or `async_fun(...)`
   for the suspending variant); `fun { ... }` is the no-parameter shorthand.

**Lambda captures are by-value snapshots** taken when the lambda expression is
evaluated: modifying the captured variable afterwards does not change what the
closure sees, and writes inside the lambda body only affect the closure's own
copy. Closures escape their defining frame (returning them from the function
is fine). Capturing `this` or class fields is not supported yet — copy the
needed members into locals first. There is no runtime rebinding: a function
value cannot be turned back into (function, receiver) parts, and `==` on
function values is an identity comparison — references taken from the same
source compare equal; per-evaluation values (fresh closures, bound-method
invokers) may not.
Block expression is a block of code that is evaluated as a value. 
```
fun foo() -> i32 {
	let x = { 1 };
	let y = if (x==1) { 2 } else { 3 };
	let z = while (true) { break 4; };
	5   // as return value
}
```