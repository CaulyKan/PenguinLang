# PenguinLang Code Generation Skill

TRIGGER when: user asks to write PenguinLang code, create .penguin files, or mentions "penguin" language syntax/features.

DO NOT TRIGGER when: user is asking about other programming languages, general programming concepts, or working with non-penguin files.

---

You are an expert in PenguinLang programming language. Help the user write correct, idiomatic PenguinLang code following these guidelines:

## Reference Documentation

When uncertain about syntax or language features, consult these authoritative sources:

### Documentation Files
- `Documentation/01_Overview.md` - Language overview and hello world
- `Documentation/02_ExecutionFlowAndEvents.md` - `initial` blocks, events (`event`, `emit`, `wait`, `on`)
- `Documentation/03_DataTypes.md` - Type system, mutability, classes, generics, type casting
- `Documentation/04_Function.md` - Functions, parameters, return values, generators, lambdas, constructors
- `Documentation/05_Enum.md` - Enum types, pattern matching with `is`
- `Documentation/06_Interface.md` - Interfaces, `impl` keyword, default implementations
- `Documentation/07_AsynchronizationAndConcurrency.md` - `async`, `wait`, `on` routines, threading model
- `Documentation/08_TimingModel.md` - Realtime and custom timing models, simulation ticks
- `Documentation/09_NamespaceAndProject.md` - Namespaces, `using`, project files
- `Documentation/10_MetaProgramming.md` - `#fun`, `const if`, `const for`, `#template`, `#can_compile`, AST manipulation

### Grammar File
- `PenguinLangParser/PenguinLang.g4` - ANTLR4 grammar defining exact syntax rules

**IMPORTANT**: If you are unsure about any syntax, READ the relevant documentation or grammar file before writing code. Do not guess syntax.

## Language Overview

PenguinLang is a concurrent-friendly programming language with C#-like syntax, inspired by C (syntax), C#/Java (GC), Rust (type system), Go (coroutines), and Verilog/SystemC (concurrency).

## Entry Point

Use `initial` blocks instead of `main`. Multiple `initial` blocks can execute concurrently:

```penguin
initial {
    println("hello world");
}
```

## Data Types

### Basic Types
- Integers: `i8`, `i16`, `i32`, `i64`, `u8`, `u16`, `u32`, `u64`
- Floats: `f32`, `f64`
- Other: `bool`, `char`, `string`, `void`

### Mutability

Three forms of variable declaration:

```penguin
let x: i32 = 10;          // immutable with explicit type
let y: mut i32 = 20;      // mutable with explicit type (mut goes on TYPE, not on let)
let mut z = 30;           // mutable with inferred type (mut goes on let, no type annotation)
let w: !mut i32 = 40;     // explicitly immutable
```

**CRITICAL**: You CANNOT combine `let mut` with an explicit type annotation. These are INVALID:
```penguin
// WRONG - compile error: "Cannot use 'let mut' with explicit type specifier"
let mut x: i32 = 10;

// CORRECT alternatives:
let x: mut i32 = 10;    // mut on the type
let mut x = 10;         // mut on let, type inferred
```

### Built-in Structures
- `Option<T>` - `new Option<T>.some(value)` or `new Option<T>.none()`
- `Result<T, E>` - `new Result<T, E>.ok(value)` or `new Result<T, E>.error(e)`
- `List<T>`, `Queue<T>`, `AtomicI64`, `StringBuilder`

## Enums

### Creating Enum Values (CRITICAL)

Enum variants are ALWAYS created with `new`:

```penguin
// CORRECT - always use 'new'
let a = new Option<i32>.some(1);
let b = new Option<i32>.none();
let tok = new TokenType.EOF();
let op = new BinaryOperator.Add();

// WRONG - without 'new', this is a TYPE reference, not a value
let a = Option<i32>.some(1);   // COMPILE ERROR
let tok = TokenType.EOF;       // COMPILE ERROR
```

### Comparing Enums (CRITICAL)

Use `is` keyword to check enum variant, NOT `==`:

```penguin
// CORRECT
if (token.token_type is TokenType.EOF) { ... }
if (a is Option<i32>.some) { ... }

// WRONG - == doesn't work for enum variant comparison
if (token.token_type == TokenType.EOF) { ... }    // COMPILE ERROR
```

### Comparing Two Enum Variables

When comparing two enum variables, use `cast<string>()`:

```penguin
// CORRECT - cast both to string for equality check
if (cast<string>(a) == cast<string>(b)) { ... }

// WRONG - can't use == directly on enum values
if (a == b) { ... }   // WON'T WORK
```

### Accessing Enum Payload

```penguin
let a: mut Option<i32> = new Option<i32>.some(42);
if (a.is_some()) {
    let value: i32 = a.some;   // access payload via field name
}
```

## Classes

```penguin
class MyClass {
    x: !mut i32;        // always immutable
    y: i32;             // follows object mutability
    z: mut i32 = 0;     // always mutable (MUST have default value)

    fun new(mut this, x: i32) {
        this.x = x;
    }

    fun instance_method(this) -> i32 {
        return this.x;
    }

    fun mutable_method(mut this) {
        this.z = 1;  // can modify fields
    }
}
```

### Constructor Rules

- Constructor is always `fun new(mut this, ...)` - `mut this` is required
- Enum type parameters must also have `mut`: `fun new(mut this, token_type: mut TokenType, text: string)`
- All class fields must have default values

## Functions

```penguin
fun regular_function(param: i32) -> i32 {
    return param * 2;
}

fun mutable_param(param: mut i32) {
    param = 10;  // OK, param is mutable
}

fun returns_mutable() -> mut MyClass {
    return new MyClass();
}

fun generator() -> mut IGenerator<i32> {
    yield 1;
    yield 2;
}

let lambda: fun<i32, i32> = fun(x: i32) -> i32 {
    return x * 2;
};
```

## Type Casting

### Using `as` (preferred for type conversion)

```penguin
let a: i32 = 1;
let b: f32 = a as f32;
let c: i64 = a;         // implicit safe cast: i32 -> i64
let d: i32 = b as i32;  // explicit unsafe cast: f32 -> i32
```

### Using `cast<T>()` (required in some contexts)

```penguin
// For comparing enum values as strings
if (cast<string>(a) == cast<string>(b)) { ... }

// For numeric type conversions where 'as' doesn't work
let idx: i64 = 5;
let elem = list.at(cast<u64>(idx));    // List.at() takes u64

let size: u64 = list.size();
let count: i64 = cast<i64>(size);       // u64 -> i64 for arithmetic
```

### Type Checking with `is`

```penguin
if (a is i32) { ... }
if (token.token_type is TokenType.EOF) { ... }
if (obj is ISyntaxNode) { ... }
```

## String Built-in Functions

These are extern functions available in the `__builtin` namespace (used without prefix):

| Function | Signature | Description |
|----------|-----------|-------------|
| `string_length` | `(s: string) -> i64` | Returns length of string |
| `string_substring` | `(s: string, start: i64, length: i64) -> string` | Extracts substring |
| `string_char_at` | `(s: string, index: i64) -> string` | Returns single character as string |
| `string_char_code` | `(s: string) -> i64` | Returns ASCII code of first character (-1 for empty string) |
| `string_find` | `(s: string, sub: string) -> i64` | Finds first occurrence, returns -1 if not found |
| `string_find_from` | `(s: string, sub: string, start: i64) -> i64` | Finds occurrence starting from position |
| `string_to_int` | `(s: string) -> i64` | Parses string to integer, returns 0 on failure |

### String Comparison Rules

- `==` and `!=` work for string equality
- `>=`, `<=`, `>`, `<` do NOT work on strings (runtime error)
- Use `string_char_code(ch) >= string_char_code("0")` for character range checks

```penguin
// CORRECT - character range check
let code: i64 = string_char_code(ch);
if (code >= string_char_code("0") && code <= string_char_code("9")) {
    // ch is a digit
}

// WRONG - no comparison operators on strings
if (ch >= "0" && ch <= "9") { ... }   // RUNTIME ERROR
```

### StringBuilder

```penguin
let sb = new mut StringBuilder();
sb.append("hello");
sb.append(" ");
sb.append("world");
let result: string = sb.to_string();
```

## List Operations

```penguin
let list = new List<i32>();
list.push(1);                          // add element
let size: u64 = list.size();           // size() returns u64!
let elem: Option<i32> = list.at(0);   // at() takes u64 index
let popped: Option<i32> = list.pop();  // remove last

// IMPORTANT: List.size() returns u64, need cast for i64 comparison
let count: i64 = cast<i64>(list.size());
if (cast<i64>(list.size()) > 5) { ... }

// IMPORTANT: List.at() takes u64, need cast from i64
let idx: i64 = 3;
let elem = list.at(cast<u64>(idx));
```

## Interfaces

```penguin
interface IBook {
    fun get_title() -> string;

    fun get_language(this: IBook) -> string {
        return "English";
    }
}

class BookA {
    impl IBook {
        fun get_title() -> string {
            return "A";
        }
    }
}

// Or outside class:
impl IBook for BookB {
    fun get_title() -> string {
        return "B";
    }
}
```

### Interface Implementation with `this`

When implementing interface methods that have `this` parameter, use `cast<>` to access concrete type:

```penguin
interface IIterator<T> {
    fun next(mut this) -> Option<T>;
}

class RangeIterator {
    start: mut i64;
    end: mut i64;

    impl IIterator<i64> {
        // Signature uses interface type for this
        fun next(this: mut IIterator<i64>) -> Option<i64> {
            // Cast to concrete type to access fields
            let self: mut Self = cast<mut Self>(this);
            if (self.start < self.end) {
                let result = new Option<i64>.some(self.start);
                self.start += 1;
                return result;
            } else {
                return new Option<i64>.none();
            }
        }
    }
}
```

**Key Points:**
- Methods with `this` parameter must be inside `impl` block
- Implementation signature uses `this: InterfaceType` (e.g., `this: IIterator<i64>`)
- Cast `this` to concrete type: `let self: mut Self = cast<mut Self>(this);`
- Interface with `mut this` requires implementation with `this: mut InterfaceType`

## Generics/Templates

```penguin
#template(T: type)
class Box {
    value: T;
    fun new(mut this, value: T) {
        this.value = value;
    }
}

let box: Box<i32> = new Box<i32>(42);
```

## Events and Concurrency

```penguin
event data_ready: i32;

initial {
    emit data_ready(42);
}

on data_ready(value: i32) {
    println("received: {}", value);
}

initial {
    let x: i32 = wait data_ready;
}
```

## Async/Coroutines

```penguin
fun async_task() -> i32 {
    wait;  // yield to scheduler
    return 1;
}

initial {
    let task: mut IFuture<i32> = async async_task();
    let result: i32 = wait task;
}
```

## Timing Model

```penguin
// Realtime
initial {
    wait(2s);
}

// Custom (simulation ticks)
initial {
    wait 1 tick;
}
```

## Meta Programming

```penguin
#fun fib(n: u32) -> u32 {
    if (n <= 1) return n;
    return fib(n-1) + fib(n-2);
}

initial {
    let x: u32 = #fib(10);  // computed at compile time
}

// Compile-time condition
const if (T == i32) {
    return 0;
}

// Compile-time loop
const for (i in range(0, N)) {
    result = result + i;
}
```

## Namespaces

```penguin
namespace MyModule {
    let b = 0;
}

using MyModule;

initial {
    b = 1;  // or MyModule.b = 1;
}
```

## Namespaces and Module-Level Functions

PenguinLang supports defining functions directly at namespace level (not inside a class):

```penguin
namespace ast {
    // Namespace-level function (preferred over Utils classes)
    fun escape_string(s: string) -> string {
        let sb = new mut StringBuilder();
        // ...
        return sb.to_string();
    }

    class MyParser {
        // Use namespace-level functions directly
        fun parse(this, sexp: string) -> mut MyParser {
            let value = extract_quoted_value(sexp, ":key ");
            // ...
        }
    }
}
```

## Naming Conventions

### Avoid `_static` Suffix
Do NOT use `_static` suffix for functions. Use simple, clear names:

```penguin
// BAD: Don't do this
fun from_sexp_static(sexp: string) -> mut SourceLocation { ... }
fun to_sexp_static(loc: SourceLocation) -> string { ... }

// GOOD: Use simple names
fun from_sexp(sexp: string) -> mut SourceLocation { ... }
fun to_sexp(loc: SourceLocation) -> string { ... }
```

### Avoid Utils Classes
Do NOT create `XXXUtils` classes with static functions. Place utility functions at namespace level:

```penguin
// BAD: Don't do this
class SexpUtils {
    fun escape_string_static(s: string) -> string { ... }
}

// GOOD: Place functions at namespace level
namespace ast {
    fun escape_string(s: string) -> string { ... }
}
```

## Using `Option<T>` Effectively

```penguin
class PrimaryExpression {
    // Use Option<T> for optional fields instead of nullable types
    identifier: mut Option<SymbolIdentifier> = new Option<SymbolIdentifier>.none();
    literal_value: string = "";

    fun to_sexp(this) -> string {
        // Check with is_some() / is_none()
        if (this.identifier.is_some()) {
            // Access value with .some
            return this.identifier.some.to_sexp();
        } else {
            return this.literal_value;
        }
    }

    fun from_sexp(sep: string) -> mut PrimaryExpression {
        let result = new mut PrimaryExpression();
        if (has_identifier) {
            // Create some value
            result.identifier = new Option<SymbolIdentifier>.some(
                SymbolIdentifier.from_sexp(symbol_sexp)
            );
        }
        // none is already the default
        return result;
    }
}
```

## Common Pitfalls

1. **`new` is required for enum variants**: `new TokenType.EOF()` not `TokenType.EOF`
2. **`is` not `==` for enum check**: `x is TokenType.EOF` not `x == TokenType.EOF`
3. **No `let mut x: Type`**: Use `let x: mut Type = value` or `let mut x = value`
4. **`List.size()` returns `u64`**: Need `cast<i64>()` for i64 arithmetic/comparison
5. **`List.at()` takes `u64`**: Need `cast<u64>()` when using i64 index
6. **No string comparison operators**: Use `string_char_code()` for character range checks
7. **Enum constructor params need `mut`**: `fun new(mut this, e: mut MyEnum)`
8. **All class fields need default values**: `field: i32 = 0;`
9. **`some`/`none` are Option methods**: `opt.is_some()`, `opt.is_none()`, `opt.some`
10. **Cast interface this to concrete type**: `let self: mut Self = cast<mut Self>(this);`

## Best Practices

1. Use `initial` for entry points, not `main`
2. Prefer immutability by default
3. Use `Option<T>` instead of null
4. Use events for cross-routine communication
5. Leverage `async`/`wait` for concurrent operations
6. Use meta functions for compile-time computations
7. File extension: `.penguin`
8. **Avoid `_static` suffix** - use simple function names
9. **Avoid `XXXUtils` classes** - place utility functions at namespace level
10. **Use `Option<T>` for optional data** - not nullable types or empty strings
11. **Interface methods in impl block** - methods with `this` go in `impl` block
12. **Read documentation when unsure** - check `Documentation/` and grammar before guessing
