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
initial {
	print("A");
	raise event a_finished;  // define an event inline
}

event b_finished;  // explicit define an event

initial {
	wait a_finished;
	print("B");
	raise b_finished;
}
	
initial {
	wait b_finished;
	print("C");
}
```

Since `wait` keyword will block execution flow until event happen,  Above code will print `ABC` definitely. 

Penguin-lang also provide `on` control block, which is similar to 'callbacks' in most other languages.
```
initial {
	raise event foo;
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

There are other keywords or functions that can create different events, for example :
* `on A or B`: called either event `A` or `B` happens
* `on A and B`: only called when `A` and `B` happenes together, not very practical in normal (real-time) event
* `on changes(a):` called every time var `a` is changed
* `on first_time(a < 0)`: only called when first time `a` is smaller than zero
* `on A + 1s`: called on 1 second after event `A` happens
* `on lasts(a, 1s)`: called when var `a` is changed, and no additional changes happened to `a`  within 1 second

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



