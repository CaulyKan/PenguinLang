# Types and Templates

This page tours PenguinLang's type system: primitives, mutability, the value/reference split, classes, interfaces, enums, and generics. It gives one working example per concept; exact rules live in the specifications ([Data Types](../specifications/03_DataTypes.md), [Class](../specifications/05_Class.md), [Enum](../specifications/06_Enum.md), [Interface](../specifications/07_Interface.md)).

## Static Typing with Inference

Types are checked at compile time. An initializer lets the compiler infer the type; a type annotation is required for declarations without one:

```penguin
let x = 1;            // i32
let y: i64 = 2;       // annotated
let mut z: mut i32 = 3;
```

## Primitive Types

| Type | Size (bytes) | Notes |
|---|---|---|
| `i8` `i16` `i32` `i64` | 1/2/4/8 | signed integers |
| `u8` `u16` `u32` `u64` | 1/2/4/8 | unsigned integers |
| `bool` | 1 | `true` / `false` |
| `char` | 4 | Unicode code point |
| `float` / `f32` | 4 | IEEE 754 single |
| `double` / `f64` | 8 | IEEE 754 double |
| `string` | reference | immutable, garbage-collected |
| `void` | 0 | no value |

`float`/`double` are the keyword spellings, `f32`/`f64` aliases. `string` is immutable: every operation that transforms a string allocates a fresh one, and assignment shares the pointer (safe because contents never change).

## Mutability

Mutability is a compile-time property of types and bindings, not a runtime property of storage. There are four declaration forms:

```penguin
let a: i32 = 1;        // immutable binding, immutable value
let b: mut i32 = 1;    // immutable binding, mutable value — b = 2 is OK
let mut c = 1;         // mutable binding, inferred type — no annotation allowed
let d: !mut i32 = 1;   // explicitly immutable value (same as the first form)
```

`mut` composes with generics, and members can fix their own mutability relative to the object:

```penguin
class Config {
    name: !mut string;   // frozen after construction, even on a mut object
    retries: mut i32;    // always writable, even on an immutable object
    port: i32;           // follows the object's mutability
}
```

Writing through a chain (`obj.field.sub = v`) is lvalue addressing — it writes the slot inside `obj` directly. Writing into a temporary (`make().field = v`) is a compile error.

## Value Types and Reference Types

Every type is one or the other:

| | Value types | Reference types |
|---|---|---|
| Who | primitives, enums, classes whose fields are all value types (auto `IValueType`) | classes with any reference-type field (auto `IReferenceType`), `string`, interfaces |
| Managed by | stack or parent data structure | garbage collector |
| Assignment | always copies | shares the reference |

```penguin
class Point { x: i32; y: i32; }          // all-value fields → value type

class Node { data: i32; next: Node; }    // reference field → reference type
```

A value-type assignment copies; mutating the copy never affects the original:

```penguin
let mut a = new Point(1, 2);
let mut b = a;      // b is a copy of a
b.x = 9;
println(cast<string>(a.x));    // 1 — a is unchanged
```

Reference assignment shares:

```penguin
let r1: mut Node = new Node();
let r2: mut Node = r1;    // same object
r2.data = 5;              // visible through r1
```

You can force a classification with the marker interfaces `impl IValueType;` or `impl IReferenceType;` — both are empty (no methods). `Box<T>` wraps a value type into a reference type when deliberate indirection is needed. Reference-to-value assignments follow mutability: mutable-to-immutable is implicit, immutable-to-mutable is rejected.

## Classes

Classes carry fields, constructors (`fun new`), instance methods (`this` as first parameter), and static functions:

```penguin
class Counter {
    count: i32 = 0;

    fun new(mut this, start: i32) {
        this.count = start;
    }

    fun increment(mut this) {      // mut this: may modify the receiver
        this.count = this.count + 1;
    }

    fun get(this) -> i32 {         // this: read-only receiver
        return this.count;
    }

    fun describe(x: i32) -> string {   // no this → static function
        return "counter value " + cast<string>(x);
    }
}

initial {
    let c: mut Counter = new Counter(10);
    c.increment();
    println(cast<string>(c.get()));                      // 11
    println(Counter.describe(c.get()));                  // static call on the class
}
```

An immutable receiver cannot call `mut this` methods. If no `fun new` is defined the compiler generates a default constructor that takes no arguments. Details: [Class specification](../specifications/05_Class.md).

## Enums

An enum is a set of named variants, each optionally carrying a payload. Construct with `new Enum.variant(args)`, test with `is`, read the payload through the variant name:

```penguin
enum LogLevel {
    debug;
    info: string;      // variant with payload
}

fun log(level: mut LogLevel) {
    if (level is LogLevel.info) {
        println("INFO " + level.info);
    } else {
        println("DEBUG");
    }
}

initial {
    log(new LogLevel.debug());                    // DEBUG
    log(new LogLevel.info("disk full"));          // INFO disk full
}
```

Enums can have methods (`fun value_or(this, ...)`) but no constructors. They are value types. `Option<T>` and `Result<T, E>` in the standard library are generic enums.

## Interfaces

Interfaces define method contracts with optional default bodies. Implement inside the class or outside with `impl ... for`:

```penguin
interface IPrintable {
    fun print_label(this: IPrintable) -> string {
        return "?";
    }
}

#template(T: type)
class Pair {
    first: T;
    second: T;
    impl IPrintable {
        fun print_label(this: IPrintable) -> string {
            let self = cast<Pair<T>>(this);       // downcast to access fields
            return cast<string>(self.first) + "," + cast<string>(self.second);
        }
    }
    fun new(mut this, a: T, b: T) {
        this.first = a;
        this.second = b;
    }
}

initial {
    let p: mut Pair<i32> = new Pair<i32>(3, 4);
    println(p.print_label());                     // 3,4
}
```

Outside-the-class implementations can target generic instantiations (`impl IPrintable for Pair<i32> { ... }`) and can be attached to primitives (`impl IStringOps for string`). Dispatch through an interface value uses a vtable. Casting a value type to an interface **boxes** it (a heap copy); casting back **unboxes**.

## Templates (Generics)

`#template` declares generic parameters. Instantiations are monomorphized — compiled separately per argument set:

```penguin
#template(T: type)
class Box {
    value: T;
    fun new(mut this, v: T) { this.value = v; }
    fun get(this) -> T { return this.value; }
}

initial {
    let b: mut Box<string> = new Box<string>("hi");
    println(b.get());                                  // hi
}
```

Template parameters are of two kinds:

* **Type parameters** (`T: type`) — stand for types; mutability flows through them (`Box<mut i32>` makes the stored value mutable).
* **Value parameters** (`N: i32`) — compile-time constants. They are evaluated by the metaprogramming engine and therefore work on the native EmperorPenguin compilers (Pass2/Pass3); the pass3-only fixed-size array `std.Array<T, N>` is built on them. Example shape:

```penguin
#template(N: i32)
enum Buffer {
    slot;
    fun size(this) -> i64 { return N; }
}

initial {
    let b = new Buffer<8>.slot();
    println(cast<string>(b.size()));    // 8
}
```

Generic functions are called with explicit type arguments when they cannot be inferred from the parameters (`filled<i32>()`).

## Casting and Type Checks

`cast<T>(x)` converts explicitly; `x is T` tests at compile time (types) or runtime (enum variants, interface instances):

```penguin
let a: i32 = 1;
let b: f64 = cast<f64>(a);              // numeric conversion
let s: string = cast<string>(a);        // any primitive → string

let n: i64 = 2;
let c: i32 = cast<i32>(n);              // narrowing needs cast

let opt = new Option<i32>.some(5);
if (opt is Option<i32>.some) { ... }    // enum variant check
```

Implicit conversions are allowed only for safe widening (`i32` → `i64`, `f32` → `f64`), primitive → `string` where a string is expected, and object → implemented interface.

## Where to Go Next

* [Data Types specification](../specifications/03_DataTypes.md) — the full mutability and assignment-compatibility rules.
* [Metaprogramming](./MetaProgramming.md) — `#template` is a layer on top of compile-time functions.
* [Async, Timing and Modules](./AsyncTimingAndModularity.md) — classes with ports become concurrent modules.
