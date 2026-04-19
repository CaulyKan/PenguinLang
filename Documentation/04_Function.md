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
	foo: i32;
}

fun hello(param1 : i32, param2 : mut i32, param3 : MyClass, param4 : mut MyClass) {
	// param1 = 0;		// ERROR: can't change an immutable param

	param2 = 1;  	// can change value of param2, 
					// but will not affect caller because i32 is copied
					
	// param3 = new MyClass(); // ERROR: can't change an immutable param
	// param3.foo = 1;			// ERROR: can't mutate an immutable param
	
	param4.foo = 1;			// OK, caller object is mutated
	param4 = new MyClass(); // OK, and caller object is changed too
}
```

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

	fun hello_myself(this: Foo) { // Instance method
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
	fun new(mut this: Foo, x : u8) {
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


### Block Expression
Block expression is a block of code that is evaluated as a value. 
```
fun foo() -> i32 {
	let x = { 1 };
	let y = if (x==1) { 2 } else { 3 };
	let z = while (true) { break 4; };
	5   // as return value
}
```