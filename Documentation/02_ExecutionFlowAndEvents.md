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
	raise a_finished; 
}

event b_finished -> string;  // define an event with data

initial {
	wait a_finished;
	print("B");
	raise b_finished("C");
}
	
initial {
	val c: string = wait b_finished;
	print(c as string);
}
```

Since `wait` keyword will block execution flow until event happen,  Above code will print `ABC` definitely. 

Penguin-lang also provide `on` control block, which is similar to 'callbacks' in most other languages.
```
event foo;

initial {
	raise foo;
}
	
on (foo) {
	print("A");
}
	
on (foo) {
	print("B");
}
	
initial {
	wait foo;
	print("C");
}
```

In above code, when event `foo` happens, the two `on foo` blocks will be executed, and in same time the code after `wait foo` will be executed, so the result is still uncertain between `ABC` or `CBA` or any combinations. 

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

on (a == 10) {
	print("a is {}", a);
}
```
Above code will print `a is 10`, because the `on` block will only happen when `a` 'changes to' 10.

Events with params
------------------
Events can take parameters, for example:
```
event foo(x: int);

initial {
	raise foo(1);
	raise foo(2);
}

on (foo(x)) {
	println("foo with x={}", x);
}
	
on (foo(2)) {	 // with pattern matching
	println("foo with x=2 only!");
}
```
The result will be (note that order of these lines is uncertain):
```
foo with x=1
foo with x=2
foo with x=2 only!
```
When using wait keyword, the params of event will be unboxed, e.g. `val x = wait foo`



