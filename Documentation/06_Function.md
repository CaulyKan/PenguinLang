## Function
Following code defines a function with no params and no return value.
```
fun hello() {
	println("hello");
}
```

### Defining Parameters
```
class MyClass {
	int foo;
}

fun hello(val param1: int, var param2: int, val param3: MyClass, var param4: MyClass) {
	param1 = 0;		// ERROR: can't change an immutable param

	param2 = 1;  	// can change value of param2, 
					// but will not affect caller because int is copied
					
	param3 = new MyClass(); // ERROR: can't change an immutable param
	param3.foo = 1;			// ERROR: can't mutate an immutable param
	
	param4.foo = 1;			// OK, caller object is mutated
	param4 = new MyClass(); // OK, and caller object is changed too
}
```

### Return Values
```
fun foo() -> int {
	return 1;
}

fun bar() -> (int, int) {
	return 1, 1;
}

initial {
	val a: int = foo();
	val b: int;
	val c: int;
	(b, c) = bar();
}
```

### Simple Function And Stateful Function
```
fun simple_function() -> string {
	return "abc";
}

// if 'wait' is used in function, the function is a stateful function
fun stateful_function() -> string {
	return wait File.read("1.txt");
}

initial {
	val a : string = simple_function();				// block until return
	val b : string = wait stateful_function();		// block until return
	var c : future<string> = stateful_function(); 	// wont block
													// notice that future<> should be mutable
	val d : string = wait c;						// block until return
}
```

### Iterator with function 
```
// if 'yield' is used in function, the function is a stateful function
// yield with finite returns
fun bar() -> int, int {
	yield 1;
	yield 2;
}

initial {
	val a : int;
	val b : int;
	(a, b) = wait all bar();	// 'wait all' is equal to 'wait'
	
	var c : option<int>;
	var ret : future<int, int> = bar();
	c = wait any ret; // 1
	c = wait any ret; // 2
	c = wait any ret; // none
}
```

```
// yield with infinite returns (iterator)
fun foo() -> int... {
	for (val v : int in 0..3) {
		yield v;
	}
}

initial {
	val b: array<int> = wait all foo(a.iter());		// b is now [1,2,3] 
	
	var c: future<int...> = foo(a.iter());
	while (true) {
		val v: option<int> = wait any c;
		if (v.is_none()) break;
		else println(v.value()); 					// prints 1,2,3
	}
	
	for (val v : int in foo(a.iter())) {			// same as above while
		println(v);									// prints: 1,2,3
	}
}
```