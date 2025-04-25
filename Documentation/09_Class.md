## Class Definition
```
Class Test {
	// declare a field in class
	// if field is declared as 'val', it's can only be assigned inline or in constuctor
	var member : u8;
	
	// 'new' is a special function which is used as constructor. 
	// if no 'new' function is defined, a default one is created
	fun new(var this, val m : u8) {
		this.member = m;
	}
	
	// if one function is a member function, name first param as 'this' without param.
	// note that 'var' or 'val' is still required, to let compiler know that if this 
	// function may modify 'this'
	fun foo(var this) {
		println(this.member as string);
	}
	
	// if first param is not 'this', the function is a static function
	fun bar() {
		println("cant access 'this' here!");
	}
	
	// initial block is automatically invoked after constructor
	initial {
		println("hello");
	}
	
	// event handlers work fine also
	on (this.member == 0) {
		println("member is 0");
	}
}

initial {
	val a = new Test(1);
	a.foo();		// ERROR: can't call foo() because 'a' is imumtable but foo() requires mutable instance
	Test.bar();
}
```

## Channel
Channels are 'variables' that changes automatically according to its dependencies. 
```
var a = 0;
channel b = a + 1;

initial {
	a = 2;
	println("b={}", b);
}
```

Above code will print `b=3`

Global channels must be assigned upon declaration, and is immutable. Channels defined in a class must be assigned in constructor or using object initializing grammar.
```
class A {
	channal test : int,
	on test > 0 {
		println ("test={}", test)
	}
}

var num = 0;
var a = new A (test: num);
initial {
	num += 1;
}
```


## Interface Definition
```
interface IHello {
	fun hello() {
		println("hi IA");
	}
}

class A {
	impl IHello;
}

class B {
	impl IHello {
		fun hello() {
			println("hi B");
		}
	}
}

initial {
	val a = new A();
	val b = new B();
	
	a.hello();
	b.hello();
}
```
Above code will print:
```
hi IA
hi B
```

### Using varaiables and events in interface
```
interface IAdd {
	var a : i8;				// uninitialized variable in interface
	var b : i8;
	fun add() { println(a+b); }
}

interface IMinus {
	var a : i8 = 2;			// default-initialized variable in interface
	var b : i8 = 1;
	fun minus() {println(a-b);}
}

class A {
	impl IAdd {
		var a : i8 = 2;		// uninitialized variable in interface must be initialized
		var b : i8 = 1;		
	}
	impl IMinus {
		var a: i8 = 3;		// this 'a' is different from 'a' in IAdd
							// 'b' can be omitted becaused it have default initial value in interface
	}
}

class B {
	var a : i8 = 1;
	var b : i8 = 1;
	var c : i8 = 0;
	impl IAdd {
		var a is B.a;		// use 'is' keyword to tie interface varaiable to class variable
		var b is B.b;
	}
	impl IMinus {
		var a is B.a;		// since 'IAdd.a' & 'IMinus.a' is both tied to 'B.a', they always share same value
		var b is c;
	}
}

initial {
	val a = new A();
	val b = new B();
	
	a.add();		// 3
	a.minus();		// 1
	b.add();		// 2
	b.minus();		// 1
}
```

Class can implement interface outside. However there are limitations: you can't use default-initialized variable any more, every variable in interface must be tied to class variable. This is because the size of class is fixed after class definition.
```
interface ITest {
	var a = 1;
	var b;
}

class Test {
	var a = 1;
	var b = 1;
}

impl ITest for Test {
	var a is Test.a;		// default-initialized variable also need to be tied outside class definition
	var b is Test.b;	
}
```

Class/Interface reference layout:

![drawio](:/ac2675f7fb2c4c068f60cd71e24bf587)