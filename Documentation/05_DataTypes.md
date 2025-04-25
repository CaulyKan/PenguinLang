## Typing System & Variables
Penguin-lang is strong typed, but also recommand user to omit explicit type definition and let compiler infer the type. Also, penguin-lang strictly differs mutable types and immutable types through `var` & `val` keywords. 
```
val a = 1;				// immutable with inferred type int
val b: int = 1; 		// immutable with explicit type int

var a = 2.0;			// mutable with inferred type int
var b: float = 2.0;		// mutable with explicit type int
```

Types in penguin-lang can be solid or reference. A solid type is always copied when performing any assignment. A reference type follows following rules when performing an assignment:
* 'var' to 'var': referenced
* 'val' to 'val': referenced
* 'val' to 'var': if the type implments `copy` trait, data is copied. Otherwise the compiler will throw an error.
* 'var' to 'val': if the type implments `copy` trait, data is copied. Otherwise the compiler will throw an error.
For non copiable, the only chance to perform the 'var' to 'val' assignment is using built-in function `replace`. There is no chance a non copiable can perform a 'val' to 'var' assignment.
```
var a1 = 1;
var a2 = a1; 					// copied
val a3 = a1;					// copied

var b1 = new MyClass();			// suppose MyClass does not implement `copy` trait
var b2 = b1;					// referenced
val b3 = b1;					// error! penguin-lang can't gurantee the immutability of f

val c1 = new MyClass();
val c2 = c1;					// referenced
var c3 = c1;					// error! penguin-lang can't gurantee the immutability of c1

var d1 = new MyClass("A");				
var d2 = d1;					// referenced
var d3 = new MyClass("B");
val d4 = replace(d1, d3);		// replace all reference to d1 with d3
								// d1 == d2 == d3 == MyClass("B")
								// d4 == MyClass("A")
```

## Basic Data Types
Penguin-lang supports following built-in basic types listed below:
| type name | size |
| --------- | ---- |
| int       | 8    |
| byte      | 1    |
| bool      | 1    |
| char      | 4    |
| float     | 4    |
| double    | 8    |

Penguin-lang also support readonly string type, which implements `copy` trait, similar to C#. 
```
var a1 = "123";
var a2 = a1;			// referenced
val a3 = a1;			// copied
val a4 = a3;			// referenced
var a5 = a3;			// copied
```
