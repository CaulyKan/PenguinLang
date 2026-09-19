# A Basic Introduction to PenguinLang

PenguinLang is a statically typed, garbage-collected programming language with C-like syntax, built-in coroutines, and a discrete timing model. Its syntax comes from C, the type system from Rust, the memory model from C#/Java, coroutines from Go, and the concurrency/timing vocabulary from Verilog/SystemC.

A PenguinLang program starts at `initial` blocks instead of a `main` function:

```penguin
initial {
    println("hello world from penguin-lang!");
}
```

To run this program you need a working compiler toolchain — see [Compiler Usage](./CompilerUsage.md). The rest of this page is a tour of the language: every example below compiles and runs on both the BabyPenguin reference compiler and the native EmperorPenguin compiler.

## Variables and Types

Variables are declared with `let`. The compiler infers types when the declaration carries an initializer:

```penguin
let x: i32 = 1;      // explicit type
let y = 2;           // inferred as i32
let mut z = 3;       // mutable binding, type inferred
z = 4;               // OK
```

Mutability is part of the type: `let y: mut i32 = 20;` is a mutable value behind an immutable binding, and `let w: !mut i32 = 30;` is explicitly immutable. Every type is either a **value type** (copied on assignment — all primitives, enums, and classes whose fields are all value types) or a **reference type** (shared on assignment, managed by the garbage collector). Details are in [Types and Templates](./TypesAndTemplates.md) and the [Data Types specification](../specifications/03_DataTypes.md).

## Functions

A function is declared with `fun`. A function whose last statement is an expression returns it implicitly; otherwise use `return`:

```penguin
fun add(a: i32, b: i32) -> i32 {
    return a + b;
}

fun greet() {
    println("hello");
}
```

Functions are values: `fun<R, P1, ...>` is a first-class function type (the first type argument is the return type), and lambdas are written `fun(x: i32) -> i32 { return x * 2; }`:

```penguin
fun twice(x: i32) -> i32 { return x * 2; }

initial {
    let f: fun<i32, i32> = twice;
    println(cast<string>(f(21)));    // 42
}
```

## Classes

A class groups fields and methods. A method is a function whose first parameter is `this`; a function without `this` inside a class is a static function:

```penguin
class Point {
    x: i32;
    y: i32;

    fun new(mut this, x: i32, y: i32) {   // constructor
        this.x = x;
        this.y = y;
    }

    fun length2(this) -> i32 {            // instance method
        return this.x * this.x + this.y * this.y;
    }
}

initial {
    let p: mut Point = new Point(3, 4);
    println(cast<string>(p.length2()));   // 25
}
```

`mut this` means the method may modify the receiver; a plain `this` method works on immutable and mutable instances alike. Because both fields of `Point` are value types, `Point` itself is a value type — assignment copies it. A class with any reference-type field is a reference type instead.

## Enums

Enums are Rust-style tagged unions: each variant may carry a payload. Check variants with `is` and read the payload through the variant name:

```penguin
#template(T: type)
enum Shape {
    circle: T;
    square: T;
}

initial {
    let s: mut Shape<i32> = new Shape<i32>.square(9);
    if (s is Shape<i32>.square) {
        println("square " + cast<string>(s.square));   // square 9
    }
}
```

The standard library ships `Option<T>` (`some`/`none`) and `Result<T, E>` (`ok`/`error`) built this way — PenguinLang has no `null`.

## Interfaces

An interface is a contract with optional default implementations, similar to a Rust trait:

```penguin
interface IHello {
    fun name(this: IHello) -> string {
        return "hello";
    }
}

class Foo {
    impl IHello;                      // uses the default implementation
}

class Bar {
    impl IHello {
        fun name(this: IHello) -> string {
            return "bar";
        }
    }
}
```

Implementations can also live outside the class (`impl IHello for Bar { ... }`), including for generic instantiations. Interfaces are the dispatch mechanism: a value whose static type is an interface calls through a vtable at runtime.

## Generics (#template)

Generic types and functions are declared with `#template`. Generics are monomorphized — each instantiation is specialized at compile time, like C++ templates:

```penguin
#template(T: type)
class Box {
    value: T;

    fun new(mut this, v: T) {
        this.value = v;
    }
}

initial {
    let b: mut Box<i32> = new Box<i32>(5);
    println(cast<string>(b.value));   // 5
}
```

Template parameters can also be values (`#template(N: i32)`), which is how fixed-size types such as `std.Array<T, N>` are expressed. See [Types and Templates](./TypesAndTemplates.md).

## Control Flow

`if` and `while` work both as statements and as expressions (the value of a block is its last expression); `for` is for-in over anything iterable:

```penguin
initial {
    let y: i32 = if (true) { 2 } else { 3 };

    for (let i: i64 in range(0, 3)) {
        print(cast<string>(i));       // 012
    }
    println("");

    // try-bind: run the body only when the Option holds a payload
    let a = new Option<i32>.some(42);
    if (let v := a.some) {
        println(cast<string>(v));     // 42
    }
}
```

Errors are values (`Result<T, E>`, `Option<T>`) plus a `panic`/`try`/`catch` mechanism for runtime failures:

```penguin
initial {
    try {
        panic("boom");
    } catch (e) {
        println(e.message);           // boom
    }
}
```

## Strings

`string` is an immutable reference type with the standard method surface (`length`, `substring`, `find`, `replace`, `split`, ...). Concatenation uses `+`, and `cast<string>(x)` converts any primitive to a string:

```penguin
initial {
    let s: string = "Hello, Penguin!";
    println(s.to_upper());                  // HELLO, PENGUIN!
    println(s.substring(7, 7));             // Penguin!

    for (let part: string in "one,two,three".split(",")) {
        print(part + "|");                  // one|two|three|
    }
    println("");
}
```

## Generators

A function that uses `yield` is a generator: it returns a `IGenerator<T>` that produces one value per `yield` and can be consumed by for-in:

```penguin
fun count_up() -> IGenerator<i32> {
    yield 1;
    yield 2;
    yield 3;
}

initial {
    for (let v: i32 in count_up()) {
        print(cast<string>(v));       // 123
    }
    println("");
}
```

Generators need coroutine support (`--enable-coroutine` on EmperorPenguin).

## Coroutines and Time

`async expr` spawns a function as a concurrent job and returns an `IFuture<T>`; `wait` parks the current routine until a future completes, a condition holds, an event fires, or a duration elapses:

```penguin
fun work() -> i32 {
    wait 2 tick;
    return 42;
}

initial {
    let task: mut IFuture<i32> = async work();
    println("before wait");
    let a: i32 = wait task;
    println("wait done " + cast<string>(a));
}

initial {
    wait 1 tick;
    println("tick 1");
}
```

This always prints:

```
before wait
tick 1
wait done 42
```

because `wait 2 tick` suspends `work` on the simulation clock while the other routine advances to tick 1. PenguinLang's scheduler is cooperative and single-threaded; all routines share one discrete clock measured in ticks. This is the foundation for writing simulations and RTL-style module systems — see [Async, Timing and Modules](./AsyncTimingAndModularity.md).

## Where to Go Next

* [Compiler Usage](./CompilerUsage.md) — building the compilers and running your first native program.
* [Types and Templates](./TypesAndTemplates.md) — the type system in depth.
* [Metaprogramming](./MetaProgramming.md) — compile-time functions, reflection, and code generation.
* [Async, Timing and Modules](./AsyncTimingAndModularity.md) — coroutines, the timing model, and modular design.
* The [specifications](../specifications/01_Overview.md) for exact definitions of each language feature.
