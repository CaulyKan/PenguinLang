## Typing System
Penguin-lang is statically typed, but also recommends users to omit explicit type definitions and let the compiler infer the type.

## Basic Data Types
Penguin-lang supports the following built-in basic types:
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

| Type            | Who                                                           | Managed By                     | Assignment       |
| --------------- | ------------------------------------------------------------- | ------------------------------ | ---------------- |
| Value types     | i32, f64, string...<br /> classes that implement `IValueType` | Stack or Parent Data Structure | Always copied    |
| Reference types | any other types                                               | GC                             | Shared reference |

## Mutability
Use `var`/`const` to declare variables, which are shorthand for annotating types.
```
var x: i32 = 1;
x = 2; // OK

const y: i32 = 1;
y = 2; // ERROR

var z : const i32 = 1;
z = 2; // ERROR, identical to above

var foo: const List<i32>;   // can't add/remove elements, nor modify elements
var bar: List<const i32>;   // can add/remove elements, but can't modify elements
```

More on mutability:
- Value types are free to be assigned regardless of mutability -- they are always copied
- Reference types:
  - immutable to mutable: not allowed unless explicitly casted, or cloned.
  - mutable to immutable: implicitly allowed

```
const x: MyClass = new MyClass;
var a = x;                  // not allowed!
var b = x.clone();          // clone may be expensive
var c = x as MyClass;       // use at your risk

var y : MyClass = new MyClass;
const d = y;                // OK
```

## Option Type
There is no `null`/`none` value, use `Option<T>` instead.

```
enum Option<T> {
    some: T,
    None,
}

## Class
Like many other programming languages, penguin-lang supports classes. 
```
Class MyClass {
	const x: i32;		// immutable field, can't be mutated after create, regardless of mutability of the object.
	var y: i32;		// mutable field, can be mutated after create if the object is mutable.
}

const a = new MyClass();
a.x = 1;			// error! x is immutable
a.y = 2;			// error! a is immutable so a.y is immutable

var b = new MyClass();
b.x = 1;			// error! x is immutable
b.y = 2;			// ok! b.y is mutable
```

## Type Casting
Penguin-lang uses `as` as the keyword for type casting.
```
var a: i32 = 1;
var b: f32 = a as f32;
```

There are some implicit type casting rules:
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
class MyClass<T> {
    var x: T;
}

var a = new MyClass<i32>();
a.x = 1;
```
