# Overview

PenguinLang is a statically typed, garbage-collected programming language with C-like syntax, built-in coroutines, and a discrete timing model. It draws on C (syntax), C#/Java (garbage collection), Rust (type system, enums, interfaces), Go (coroutines), and Verilog/SystemC (concurrency and timing vocabulary).

This chapter defines the smallest complete program: the entry point, console output, functions, and variable declarations.

## Program Structure

A PenguinLang program is a set of source files (`.penguin`). Execution starts at **`initial` blocks**, not a `main` function. A program may contain any number of `initial` blocks; they are independent routines that run concurrently under the scheduler (see [Async & Timing Model](./09_AsyncAndTimingModel.md)):

```penguin
initial {
    println("hello world from penguin-lang!");
}
```

Each source file may also declare namespaces, functions, classes, enums, interfaces, global variables, and `construct` wiring blocks (see [Namespace & Project](./08_NamespaceAndProject.md) and [Modular Programming](./10_ModularProgramming.md)). Global variables are assigned before simulation starts, in dependency order; `construct` blocks run at elaboration time; then every `initial` routine is spawned.

A program terminates when all initial routines finish or the scheduler reaches quiescence (every routine parked, nothing can wake anything) — exit code 0. `exit(code)` terminates immediately.

## Console Output

`print`/`println` write to stdout (`eprint`/`eprintln` write to stderr); `println` appends a newline. Arguments are strings:

```penguin
print("A");
println("B");                       // prints "AB\n"
println("n=" + cast<string>(42));   // n=42
```

`cast<string>(x)` converts any primitive to its string form. On the native runtime `std.io.print` is the same function (see [Namespace & Project](./08_NamespaceAndProject.md) for the `std.io` library).

## Functions

A function is declared with `fun name(params) -> ret { ... }`:

```penguin
fun add(a: i32, b: i32) -> i32 {
    return a + b;
}

fun greet() {
    println("hello");
}
```

The return type annotation is optional when the body's final expression or the `return` statements determine it. A function whose last statement is an expression returns it implicitly. Functions can be generic (`#template`), async, lambdas, or values — the full rules are in [Function](./04_Function.md).

## Variable Declarations

Variables are declared with `let` in four forms:

| Form | Meaning |
|---|---|
| `let x: T = v;` | immutable binding to an immutable value |
| `let x: mut T = v;` | immutable binding to a **mutable** value (`mut` on the type) |
| `let mut x = v;` | **mutable binding**, type inferred — no type annotation allowed |
| `let x: !mut T = v;` | explicitly immutable value (same as the first form) |

```penguin
let a: i32 = 1;        // a = 2 would be a compile error
let b: mut i32 = 1;    // b = 2 is OK
let mut c = 1;         // c = 2 is OK; type inferred as i32
let d: !mut i32 = 1;   // explicitly immutable
```

`let mut x: i32 = 1;` (mut on `let` *and* a type annotation) is a compile error. The type may be omitted only when an initializer is present. The complete mutability and assignment-compatibility rules — including members, generics, and parameters — are defined in [Data Types](./03_DataTypes.md).

Declarations are also allowed without an initializer (`let b: mut i32;`), assigned later. There is no `null`: absence is expressed with `Option<T>` (`some`/`none`, see [Enum](./06_Enum.md)).

## Vocabulary

| Concept | Where |
|---|---|
| Types, mutability, value/reference semantics | [Data Types](./03_DataTypes.md) |
| Control flow statements and expressions | [Basic Execution Flow](./02_BasicExecutionFlow.md) |
| Functions, lambdas, generators | [Function](./04_Function.md) |
| Classes | [Class](./05_Class.md) |
| Enums (`Option`, `Result`) | [Enum](./06_Enum.md) |
| Interfaces and dispatch | [Interface](./07_Interface.md) |
| Namespaces, projects, libraries | [Namespace & Project](./08_NamespaceAndProject.md) |
| Coroutines and the timing model | [Async & Timing Model](./09_AsyncAndTimingModel.md) |
| Modules, ports, channels | [Modular Programming](./10_ModularProgramming.md) |
| Compile-time code | [Meta Programming](./11_MetaProgramming.md) |
