## Typing System 
Penguin-lang is strong typed, but also recommand user to omit explicit type definition and let compiler infer the type. 

## Basic Data Types
Penguin-lang supports following built-in basic types listed below:
| type name | size |
| --------- | ---- |
| i8        | 1    |
| i16       | 2    |
| i32       | 4    |
| i64       | 8    |
| u8        | 1    |
| u16       | 2    |
| u32       | 4    |
| u64       | 8    |
| bool      | 1    |
| char      | 4    |
| f32       | 4    |
| f64       | 8    |
| void      | 0    |

Penguin-lang also support readonly string type. This means the string is not mutable after create, similar to C#. However, the string is still a reference type, and can be passed to other functions as reference.

## Reference Types and Value Types
Penguin-lang supports both reference types and value types. A reference type is a type that holds a reference to an object, and can be passed to other functions as reference. A value type is a type that holds its own data, and can be copied when assigned to another variable.

Following types are value types:
* all basic types
* string
* any class that implements `ICopy` interface

Any other type is reference type.

## Mutability
Also, penguin-lang strictly differs mutable types and immutable types through `var` & `val` keywords in decalration. 
```
val a = 1;				// immutable with inferred type int
val b: i32 = 1; 		// immutable with explicit type int

var a = 2.0;			// mutable with inferred type int
var b: f32 = 2.0;		// mutable with explicit type int
```

Types in penguin-lang can be solid or reference. A solid type is always copied when performing any assignment. A reference type follows following rules when performing an assignment:
- Value types are free to be assigned regardless of mutability -- they are always copied
- Reference types:
 - immutable to mutable: not allowed unless explicitly casted or cloned.
 - mutable to immutable: implicitly allowed

Some examples for value types:
```
var a1 = 1;
var a2 = a1; 					// copied
val a3 = a1;					// copied
```

`val` to `var` assignment for reference types:
```
val c1 = new MyClass();
val c2 = c1;					// referenced
var c3 = c1;					// error! penguin-lang can't gurantee the immutability of c1
```

`var` to `val` assignment for reference types:
```
var b1 = new MyClass();			// suppose MyClass does not implement `ICopy`
var b2 = b1;					// referenced
val b3 = b1;					// compiler can determine the lifetime of b1/b3, referenced

fun foo(val b4: MyClass) { }	// suppose lifetime of b4 is not extended beyond foo()
foo(b1);						// compiler can determine the lifetime of b1/b4, referenced

event MyEvent : MyClass;		
emit MyEvent(b1);				// compiler can't determine the lifetime of MyEvent, so a RW lock is generated
```

We will cover more details on Asynchronization chapter.

## Class
Like many other programming languages, penguin-lang supports classes. 
```
Class MyClass {
	val x: i32;		// immutable field, can't be mutated after create, regardless of mutability of the object.
	var y: i32;		// mutable field, can be mutated after create if the object is mutable.
}

val a = new MyClass();
a.x = 1;			// error! x is immutable
a.y = 2;			// error! a is immutable so a.y is immutable

var b = new MyClass();
b.x = 1;			// error! x is immutable
b.y = 2;			// ok! b.y is mutable
```

## Type Casting
Penguin-lang use `as` as keyword for type casting.
```
val a: i32 = 1;
val b: f32 = a as f32;	// type casting from i32 to f32
```

There are exists some implicit type casting rules:
 * safe basic type casting, including i32 to i64, f32 to f64, etc.
 * basic type to string casting
 * object to interface casting, if the object implements the interface.

```
var a: i32 = 1;
var b: i64 = 2;

b = a; // OK 
a = b; // compile error
a = b as i32; // OK, but may lose data
```

## Generics
Penguin-lang supports generics, which means a type can be parameterized with other types.
```
Class MyClass<T> {
	var x: T;
}

val a = new MyClass<i32>();
a.x = 1;
```
