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
Penguin-lang features a strong, explicit, and fine-grained mutability system enforced at compile time. This design aims to prevent accidental mutations and promote safer, more predictable code.

### Variable Declaration and Mutability Keywords
Variables are declared using `let`. By default, `let` declares an **immutable** variable. To make a variable mutable, use the `mut` keyword.

*   **`let`**: Declares an immutable variable.
    ```penguin
    let x: i32 = 10; // x is immutable
    x = 20;          // Compile-time ERROR: Cannot reassign immutable variable
    ```
*   **`mut`**: Explicitly marks a variable or type as mutable.
    ```penguin
    let y: mut i32 = 20; // y is mutable
    y = 30;              // OK: Can reassign mutable variable
    ```
*   **`!mut`**: Explicitly marks a type as immutable. This is used to enforce immutability in contexts where `mut` might be the default or to explicitly state immutability.
    ```penguin
    let z: !mut i32 = 30; // z is explicitly immutable
    z = 40;               // Compile-time ERROR
    ```

### Class Member Mutability
Class members can also be declared with `mut` or `!mut`.

*   **Default (Implicit Immutable)**: If a class member is declared without `mut` or `!mut`, its mutability is aligned with its containing object.
    ```penguin
    class MyClass {
        a: i32 = 1; // 'a' is implicitly immutable
    }
    let obj: MyClass = new MyClass();
    obj.a = 2; // Compile-time ERROR: Cannot assign to immutable member
    let obj2: mut MyClass = new MyClass();
    obj2.a = 2; // OK
    ```
*   **Explicitly Mutable Member**:
    ```penguin
    class MyClass {
        b: mut i32 = 1; // 'b' is explicitly mutable
    }
    let obj: MyClass = new MyClass();
    obj.b = 2; // OK: 'b' is mutable, even if 'obj' is immutable
    ```
*   **Explicitly Immutable Member**:
    ```penguin
    class MyClass {
        c: !mut i32 = 1; // 'c' is explicitly immutable
    }
    let obj: mut MyClass = new MyClass();
    obj.c = 2; // Compile-time ERROR: Cannot assign to explicitly immutable member
    ```

### Mutability and Generics
Mutability can be applied to generic type parameters and members.

*   **Generic Member Mutability**:
    ```penguin
    #template(T: type)
    class Box {
        value: T; // 'value' inherits mutability from 'T'
    }
    initial {
        let b: Box<mut i32> = new Box<mut i32>(1); // 'value' inside 'b' is mutable
        b.value = 2; // OK
    }
    ```
*   **Explicit Mutability for Generic Members**:
    ```penguin
    #template(T: type)
    class Container {
        data: mut T; // 'data' is always mutable, regardless of 'T'
        data2 : !mut T; // 'data2' is always immutable, regardless of 'T'
    }
    initial {
        let c: Container<i32> = new Container<i32>(1);
        c.data = 2; // OK
    }
    ```
*   **Auto Mutability for Generic Members**: The `auto` keyword aligns the mutability of a member with its container object, regardless of the mutability of the generic type `T`.
    ```penguin
    #template(T: type)
    class Container {
        data: auto T; // 'data' mutability is aligned with its container object, regardless of 'T'
    }
    initial {
        let c: mut Container<i32> = new Container<i32>(1);
        c.data = 2; // OK, because 'c' is mutable

        let c2: Container<mut i32> = new Container<mut i32>(1);
        c2.data = 2; // Compile-time ERROR, because c2 is immutable
    }
    ```


### Assignment Compatibility
Penguin-lang has strict rules for assigning values between variables of different mutability.

*   **Value Types**: Value types are always copied on assignment, so their mutability does not affect assignment compatibility.
    ```penguin
    let a: i32 = 1;
    let b: mut i32;
    b = a; // OK: 'a's value is copied to 'b'
    ```
*   **Reference Types**:
    *   **Mutable to Immutable (Subsequent Assignment)**: Not allowed. An immutable variable cannot be reassigned to a mutable reference after its initial declaration.
        ```penguin
        let a: mut MyClass = new MyClass();
        let b: MyClass;
        b = a; // Compile-time ERROR: Cannot reassign immutable variable 'b'
        ```
    *   **Immutable to Mutable**: Not allowed unless in initialization. A mutable variable cannot be assigned an immutable reference. This prevents "upgrading" an immutable reference to a mutable one, which could then be used to mutate an object intended to be immutable.
        ```penguin
        let a: MyClass = new MyClass();
        let b: mut MyClass = a; // OK
        b = a; // Compile-time ERROR
        ```

### Function Call Mutability
Function parameters can specify their expected mutability.

*   **Parameter Mutability**:
    ```penguin
    fun foo(a: MyClass, b: mut MyClass) {
        // 'a' is immutable within foo, 'b' is mutable
    }
    initial {
        let x: MyClass = new MyClass();
        let y: mut MyClass = new MyClass();
        foo(x, y); // OK
        foo(y, x); // Compile-time ERROR: Cannot pass immutable 'x' to mutable parameter 'b'
    }
    ```
*   **`this` Mutability in Methods**: Methods can specify the mutability of the instance (`this`) they are called on.
    *   `fun myMethod(this)`: This method can only be called on immutable or mutable instance. It cannot modify the instance.
    *   `fun myMutableMethod(mut this)`: This method can only be called on a mutable instance. It is allowed to modify the instance.
    ```penguin
    class Example {
        value: i32 = 0;
        fun get_value(this) {
            print(this.value as string);
        }
        fun set_value(mut this, new_value: i32) {
            this.value = new_value;
        }
    }
    initial {
        let immutable_ex: Example = new Example();
        immutable_ex.get_value(); // OK
        immutable_ex.set_value(1); // Compile-time ERROR: Cannot call mutable method on immutable instance

        let mutable_ex: mut Example = new Example();
        mutable_ex.get_value(); // OK
        mutable_ex.set_value(1); // OK
    }
    ```

## Built-in Data Structures

Penguin-lang provides several built-in data structures.

*   **`Option<T>`**: Represents an optional value. It can be either `Some(T)` or `None`. This is used instead of `null` to handle the absence of a value safely.
    ```penguin
    #template(T: type)
    enum Option {
        some: T,
        None,
    }
    ```
*   **`Result<T, E>`**: Used for returning and propagating errors. It can be either `Ok(T)` or `Error(E)`.
*   **`List<T>`**: A growable, heap-allocated list.
*   **`Queue<T>`**: A queue.

## `Self` Type
The `Self` keyword can be used in a class or interface to refer to the type of the current class or interface.

```penguin
interface IFoo {
    fun a() -> Self;
}

class Foo {
    fun a() -> Self {
        return new Foo();
    }
}
```

## Type Aliases
The `type` keyword can be used to create a new name for an existing type.

```penguin
type MyInt = i32;

let x: MyInt = 10;
```

## Class
Like many other programming languages, penguin-lang supports classes. 
```
class MyClass {
	x: !mut i32;		// immutable field, can't be mutated after create, regardless of mutability of the object.
	y: i32;		// mutable field, can be mutated after create if the object is mutable.
}

let a: MyClass = new MyClass();
a.x = 1;			// error! x is immutable
a.y = 2;			// error! a is immutable so a.y is immutable

let b: mut MyClass = new MyClass();
b.x = 1;			// error! x is immutable
b.y = 2;			// ok! b.y is mutable
```

## Type Checking and Casting
Penguin-lang uses `as` as the keyword for type casting. The `is` keyword is used for type checking.

```
var a: i32 = 1;
var b: f32 = a as f32;

if (a is i32) {
    // ...
}
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

## Templates (Generics)
Penguin-lang supports templates (generics), which allow types and functions to be parameterized. The declaration uses the `#template` keyword.
```penguin
#template(T: type)
class MyClass {
    x: T;
}

let a: MyClass<i32> = new MyClass<i32>();
a.x = 1;
```
