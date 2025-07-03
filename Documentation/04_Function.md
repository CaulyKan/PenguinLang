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
	val foo: i32;
}

fun hello(const param1: i32, var param2: i32, const param3: MyClass, var param4: MyClass) {
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

Return value also has mutability. By default, return value is mutable. To make it immutable, use `const` keyword.
```
fun clone(const foo: Foo) -> Foo {
	return new Foo(foo.x, foo.y);
}

fun borrow(const foo: Foo) -> const Foo {
	return foo;
}
```

### Generator Function
PenguinLang supports generator functions.
```
fun test() -> IGenerator<i32> {
	yield 1;
	yield 2;
	yield 3;
}

initial {
	for (const v : i32 in test()) {
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
	val name: string = "Foo";
	fun hello_world() {
		println("hello");
	}

	fun hello_myself(const this: Foo) {
		println("hello " + this.name);
	}
}
```

Note that if the first parameter is 'this', its type should be the class itself, and must specify mutability. If the first parameter is not 'this', the function is similar to a static function.
```
var foo = new Foo();
foo.hello_myself();		// OK, 'foo' instance is passed as 'this' parameter
foo.hello_world();		// OK, however 'foo' instance is not accessible in the function
Foo.hello_world();		// OK, function can be called without an instance
```

### Constructor
Function 'new' is used as constructor in class. The first parameter must be mutable 'this', which is the instance of the class being created. If no 'new' function is defined, a default one is created.
```
class Foo {
	val x: u8 = 1;
	fun new(var this: Foo, const x : u8) {
		this.x = x;
	}
}

initial {
	var foo = new Foo(2);
}
```

Immutable class members can only be initialized in the constructor or variable declaration.

### Lambda Function
Lambda function is a function that is defined inline in a block of code.
```
fun foo() {
	var f : fun<i32, i32> = fun(const x: i32) -> i32 {
		return x * 2;
	};
	println(f(3) as string);
}
```

