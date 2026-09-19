# Class

This chapter defines classes: fields, constructors, methods, static functions, and generic classes. A class is the unit of data abstraction; with ports it also becomes a module (see [Modular Programming](./10_ModularProgramming.md)).

## Declaration

```penguin
class Point {
    x: i32;
    y: i32;
}
```

A class consists of field declarations, functions (methods), `impl` blocks (interface implementations, see [Interface](./07_Interface.md)), port declarations (see [Modular Programming](./10_ModularProgramming.md)), and nested `initial`/`construct` blocks. Members without an explicit `impl`/port form are fields and functions.

## Fields

A field declares `name : [mut|!mut] type [= initializer]`. Mutability of a field without an explicit marker follows the containing object:

```penguin
class MyClass {
    a: i32 = 1;        // follows the object: writable on a mut object only
    b: mut i32 = 1;    // explicitly mutable: writable even on an immutable object
    c: !mut i32 = 1;   // explicitly immutable: frozen after construction
}

initial {
    let obj : MyClass = new MyClass();
    // obj.a = 2;      // ERROR: obj is immutable so a is immutable
    obj.b = 2;         // OK
    // obj.c = 2;      // ERROR: c is explicitly immutable

    let obj2 : mut MyClass = new MyClass();
    obj2.a = 2;        // OK: object is mutable, a follows
}
```

Immutable members (`!mut`, or any member of an immutable object) can only be assigned in the constructor or in the field's own initializer.

## Constructors

The constructor is the function named `new`. Its first parameter must be `mut this`; parameters follow after it. A class has at most one constructor — overloading `new` is a compile error (`E_DUPLICATE_SYMBOL`):

```penguin
class Point {
    x: i32;
    y: i32;

    fun new(mut this, x: i32, y: i32) {
        this.x = x;
        this.y = y;
    }
}

initial {
    let foo : mut Point = new Point(1, 2);
}
```

If a class defines no `fun new`, the compiler generates a **default constructor that takes no arguments** — every field gets its initializer value or the type's zero value. Calling the default constructor with arguments is a compile error:

```penguin
class Unit { count: i32 = 5; }

initial {
    let u : mut Unit = new Unit();        // OK, count = 5
    // let v = new Unit(3);               // ERROR: default constructor takes no arguments
}
```

## Methods and Static Functions

A function inside a class is a **method** when its first parameter is `this`, otherwise it is a **static function**:

```penguin
class Foo {
    name: string = "Foo";

    fun hello_world() {            // static function: no this
        println("hello");
    }

    fun hello_myself(this) {       // instance method: read-only receiver
        println("hello " + this.name);
    }

    fun rename(mut this, n: string) {   // instance method: mutable receiver
        this.name = n;
    }
}

initial {
    let mut foo = new Foo();
    foo.hello_myself();     // OK, 'foo' instance is passed as 'this'
    foo.rename("Bar");      // OK, mut receiver
    foo.hello_world();      // OK, static functions can be called on an instance
    Foo.hello_world();      // OK, and directly on the class
}
```

* `this` — the method reads the receiver; callable on immutable and mutable instances.
* `mut this` — the method may write the receiver; callable on mutable instances only.
* A method without a receiver is static; it cannot touch instance fields and is invoked as `Class.name(...)` (or through an instance).

Inside a method, `this.field` accesses fields; plain `field` also resolves to members. `Self` refers to the enclosing class's type:

```penguin
class Counter {
    value: i32 = 0;
    fun bumped(this) -> Self {
        let c : mut Counter = new Counter();
        return c;
    }
}
```

## Value and Reference Classes

Every class is classified as a value type or a reference type (full rules in [Data Types](./03_DataTypes.md)): all-value fields → value type (auto `IValueType`, copies on assignment); any reference field → reference type (auto `IReferenceType`, shared on assignment). `impl IValueType;` / `impl IReferenceType;` override the automatic classification. Value classes get an auto-generated `ICopy<Self>` memberwise copy unless they define their own.

## Generic Classes

`#template` declares generic parameters on a class; each instantiation is monomorphized (compiled separately per argument set):

```penguin
#template(T: type)
class Box {
    value: T;

    fun new(mut this, v: T) {
        this.value = v;
    }
}

initial {
    let b : mut Box<i32> = new Box<i32>(1);   // Box<i32> is a distinct specialized type
}
```

Specialized types carry the argument list in their full name (`Box<i32>`). Template parameters are type parameters (`T: type`) or compile-time value parameters (`N: i32` — native compilers, see [Meta Programming](./11_MetaProgramming.md)); mutability flows through type parameters (`Box<mut i32>` stores a mutable value).

Methods may declare their own template parameters, specialized per call-site type arguments (implemented in the EmperorPenguin compilers — Pass1 through Pass3; the BabyPenguin reference compiler does not support method-level templates):

```penguin
class Picker {
    #template(U: type)
    fun pick(mut this, x: U) -> U { return x; }
}

initial {
    let p : mut Picker = new Picker();
    println(p.pick<string>("hey"));    // hey
}
```

A generic class's body is bound once per instantiation — template code inside it may use `#if`/`#fun` metaprogramming that sees the concrete arguments.

## Enums, Interfaces, Impls Inside Classes

* An `impl IX { ... }` block inside a class implements an interface for that class ([Interface](./07_Interface.md)).
* A nested `initial { ... }` block makes instances of the class concurrent routines (each instance spawns its initial at construction — the module pattern).
* A nested `construct { ... }` block runs at `new` time, after the constructor, before the instance's initials spawn — the only place `connect` is legal.
