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

fun hello(param1: i32, param2: mut i32, param3: MyClass, param4: mut MyClass) {
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
	var a: i32 = foo();
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
PenguinLang supports generator functions.
```
fun test() -> mut IGenerator<i32> {
	yield 1;
	yield 2;
	yield 3;
}

initial {
	for (let mut v : i32 in test()) {
		println(v as string);
	}
}
```

### Simple Function And Stateful Function
Function can be simple or stateful. 
If 'wait' is used in function, or the function calls a stateful function, the function is a stateful function.
If a function is a generator function, it is a stateful function.
We will cover this topic in asynchonous chapter.

### Function in Class
Class can define functions.
```
class Foo {
	name: string = "Foo";
	fun hello_world() {
		println("hello");
	}

	fun hello_myself(this: Foo) {
		println("hello " + this.name);
	}
}
```

Note that if the first parameter is 'this', its type should be the class itself, and must specify mutability. If the first parameter is not 'this', the function is similar to a static function.
```
let mut foo = new Foo();
foo.hello_myself();		// OK, 'foo' instance is passed as 'this' parameter
foo.hello_world();		// OK, however 'foo' instance is not accessible in the function
Foo.hello_world();		// OK, function can be called without an instance
```

### Constructor
Function 'new' is used as constructor in class. The first parameter must be mutable 'this', which is the instance of the class being created. If no 'new' function is defined, a default one is created.
```
class Foo {
	x: u8 = 1;
	fun new(let this: mut Foo, x : u8) {
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
	println(f(3) as string);
}
```

