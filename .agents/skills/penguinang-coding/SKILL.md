# PenguinLang Code Generation Skill

TRIGGER when: user asks to write PenguinLang code, create .penguin files, or mentions "penguin" language syntax/features.

DO NOT TRIGGER when: user is asking about other programming languages, general programming concepts, or working with non-penguin files.

---

You are an expert in PenguinLang programming language. Help the user write correct, idiomatic PenguinLang code following these guidelines.

## CRITICAL: Two Writing Contexts

Before writing ANY `.penguin` code, identify which target you are writing for. The allowed syntax differs dramatically:

| | Context A: EmperorPenguin compiler sources | Context B: User applications & tests |
|---|---|---|
| **Files** | `EmperorPenguin/src/**`, `EmperorPenguin/main.penguin`, `EmperorPenguin/std/penguin/*.penguin` | everything else — `Examples/`, `Tests/*.md` code, user projects |
| **Compiled by** | BabyPenguin (ANTLR grammar) at bootstrap pass1, then by itself | BabyPenguin VM and/or EmperorPenguin pass2+ |
| **Syntax level** | **restricted subset: "plain penguin + `#template`" only** | **full language** |
| **Meta programming** | ❌ none (except `#template`) | ✅ all (`#fun`, `#if`, `#while`, `#specializing`, …) |
| **Namespaces** | flat only (`emperor`, `_utils`) | flat + nested, `using`, `export` |
| **Concurrency** | ❌ none | ✅ `Event<T>`, channels, ports, `wait`, `async` |

**Why the restriction exists**: the compiler bootstraps — pass1 compiles the compiler source set (`EmperorPenguinPass1.penguins`) with BabyPenguin, whose ANTLR grammar (`PenguinLangParser/PenguinLang.g4`) only understands `#template` as a `#`-directive, has no `using`/`export`/`unsafe_cast`/`#fun`/`#if`, and cannot resolve nested-namespace member access. Every later pass can only digest what the previous pass compiles. Full details in the [EmperorPenguin Compiler Subset](#emperorpenguin-compiler-subset-restricted) section below.

When a compiler source needs a feature outside the subset, the established pattern is a **stub** (see `MetaConfigStub.penguin` / `DynlibStub.penguin`): keep the public API identical, hide the feature behind it, and swap in the real implementation at a later pass.

## Reference Documentation

When uncertain about syntax or language features, consult these authoritative sources (in this order):

1. `Tests/<Category>/*.md` — the cross-compiler test suite is the most up-to-date syntax reference; find a test in the relevant category and copy its style
2. `EmperorPenguin/std/penguin/core_builtin.penguin` — canonical, current real-world penguin code (stdlib)
3. `Documentation/` — `01_Overview` … `06_Interface`, `09_NamespaceAndProject`, `10_MetaProgramming` (most recent), `11_PortsChannelsEvents` (current concurrency model)
4. `PenguinLangParser/PenguinLang.g4` — ANTLR grammar (BabyPenguin front-end; a SUBSET of the EmperorPenguin front-end)
5. `EmperorPenguin/src/ast/Lexer.penguin` + `Parser.penguin` — the leading-edge front-end (has `using`, `export`, `unsafe_cast`, `#`-meta)

**IMPORTANT**: Some `Documentation/*.md` snippets are STALE (they show `match/case`, printf-style `println("{}", x)`, `const` fields, `on` routines — none of these exist). When docs and `Tests/` disagree, trust `Tests/` and the grammar. If unsure about any syntax, READ the sources before writing code. Do not guess syntax.

**IMPORTANT**: `as`-style casts do NOT exist. There is NO string interpolation. See pitfalls below.

## Entry Point

Use `initial` blocks instead of `main`. Multiple `initial` blocks execute concurrently (order is scheduler-dependent; don't rely on it):

```penguin
initial {
    println("hello world");
}
```

## Data Types

### Basic Types
- Integers: `i8`, `i16`, `i32`, `i64`, `u8`, `u16`, `u32`, `u64`
- Floats: `float` (f32) and `double` (f64) — `f32`/`f64` spellings are also accepted as aliases
- Other: `bool`, `char`, `string` (reference type, immutable), `void`
- Type alias: `type MyInt = i32;`

### Mutability

Four forms of variable declaration:

```penguin
let x: i32 = 10;          // immutable binding, explicit type
let y: mut i32 = 20;      // immutable binding to MUTABLE value (mut on TYPE)
let mut z = 30;           // mutable binding, inferred type (mut on let)
let w: !mut i32 = 40;     // explicitly immutable value
```

**CRITICAL**: You CANNOT combine `let mut` with an explicit type annotation — on ANY compiler, including in for-loops:
```penguin
// WRONG - compile error: "Cannot use 'let mut' with explicit type specifier"
let mut x: i32 = 10;
for (let mut i : i64 in list) { ... }

// CORRECT alternatives:
let x: mut i32 = 10;    // mut on the type
let mut x = 10;         // mut on let, type inferred
```

### Built-in Structures (auto-loaded from core_builtin for every program)
- `Option<T>` — `new Option<T>.some(value)` / `new Option<T>.none()`, `.is_some()`, `.is_none()`, `.value_or(d)`, payload via `.some`
- `Result<T, E>` — `new Result<T, E>.ok(value)` / `new Result<T, E>.error(e)`, `.is_ok()`, `.is_error()`, `.value_or(d)`
- `StringBuilder`, `Box<T>`, `Pair<K,V>`, `range(start, end)` → `RangeIterator`

## Control Flow

### if / else — statement AND expression

```penguin
if (x > 0) { ... } else if (x == 0) { ... } else { ... }

// as expression (value = last expression of the executed branch):
let y: i32 = if (x == 1) { 2 } else { 3 };
```

### for — for-IN only (no C-style for)

```penguin
for (let i : i64 in range(0, 3)) {
    println(cast<string>(i));
}

// loop var may omit the type; mut on let selects the mutable iterator path
for (let item in list) { ... }             // uses iter()
for (let mut item in list) { ... }         // uses iter_mut()
// an expression that is ALREADY an iterator is used as-is:
for (let line : string in std.io.lines(path)) { ... }
```

The iterable desugars to `iter()`/`iter_mut()` (chosen by loop-var mutability) over `IIterator<T>.next() -> Option<T>`. Containers implement `IIterable<T>` to be directly for-in-able.

### while — also usable as expression (value comes from `break expr;`)

```penguin
let found: i64 = while (true) {
    if (cond) { break 42; }
}
```

### try-bind (payload extraction, `:=`)

```penguin
if (let v := opt.some) {
    // v is bound to the payload; body runs only if it was readable
} else {
    // optional else branch
}
// with optional type: if (let v: i32 := opt.some)
// NOTE: `:=` binds at bitwise-or precedence: `let a := b && c` parses as `(let a := b) && c`
```

### try / catch

```penguin
try {
    panic("boom");            // panic throws a RuntimeError
} catch (e) {
    println(e.message);       // RuntimeError has .message and .code
}
```

There is **no `match`/`case` statement** — use `is` + if/else chains.

## Functions

```penguin
fun regular_function(param: i32) -> i32 {
    return param * 2;
}

fun mutable_param(param: mut i32) {
    param = 10;               // OK, param is mutable
}

fun returns_mutable() -> mut MyClass {
    return new MyClass();
}

// specifiers: pure fun f(); !pure fun g(); async fun h(); !async fun k();

// lambda + function type (FIRST type arg = return type, rest = param types)
let lambda: fun<i32, i32> = fun(x: i32) -> i32 {
    return x * 2;
};
let noop: fun<void> = fun { print("hi"); };

// generator
fun gen() -> mut IGenerator<i32> {
    yield 1;
    yield 2;
    return 3;                 // optional final return
}

// extern (FFI) - see Namespaces section for symbol routing
extern fun abs(x: double) -> double;
```

### `this` conventions
- `fun method(this)` — immutable receiver
- `fun method(mut this)` — mutable receiver (writes are visible to caller)
- Constructor is ALWAYS `fun new(mut this, ...)` — `mut this` required
- Inside an `impl` block the signature types the receiver: `fun next(this: mut IIterator<i64>) -> Option<i64>`
- `Self` is a type keyword (the enclosing class/interface type), but the codebase convention is to cast to the concrete type explicitly: `let self = cast<ConcreteType>(this);` (lowercase `self` is a normal identifier)

## Classes

```penguin
class MyClass {
    x: !mut i32 = 0;        // ALWAYS immutable (set once in constructor)
    y: i32 = 0;             // follows instance mutability
    z: mut i32 = 0;         // ALWAYS mutable

    fun new(mut this, x: i32) {
        this.x = x;
    }

    fun instance_method(this) -> i32 {
        return this.x;
    }

    fun mutable_method(mut this) {
        this.z = 1;
    }

    impl ISomething {       // impl block inside the class
        fun get(this: ISomething) -> string { return "x"; }
    }

    impl __builtin.IReferenceType;   // marker: force reference semantics (GC heap)
    impl __builtin.ICopy<MyClass> for MyClass;   // marker: force value semantics (memberwise copy)
}
```

**Rules:**
- Every field needs a default value (`field: i32 = 0;`) — the default constructor depends on it
- Value vs reference semantics: `impl ICopy<T>` (or all-value fields) → value type (copied on assignment); otherwise → reference type. `impl IReferenceType` forces reference semantics explicitly
- `input x : i64;` / `output y : i64;` port declarations turn the class into a module (see Concurrency) — ports carry no `mut` modifier

## Enums

### Declaration (payloads via `name: Type;`)

```penguin
// WRONG - generics are #template (see Generics section), never enum Option<T>
// CORRECT:
#template(T: type)
enum Option {
    some: T;
    none;
}

enum TokenType { EOF; Identifier; ... }          // plain variants
enum BoundDefinition { function_def: BoundFunctionDefinition; class_def: BoundClassDefinition; }
```

Enums may contain methods and impl blocks. Enums have NO constructor.

### Creating Enum Values (CRITICAL) — `new` is ALWAYS required

```penguin
// CORRECT - always 'new', with parens even for no-arg variants
let a = new Option<i32>.some(1);
let b = new Option<i32>.none();
let tok = new TokenType.EOF();

// WRONG - without 'new' this is a TYPE reference, compile error
let a = Option<i32>.some(1);
let tok = TokenType.EOF;
```

### Checking & Comparing Enums

```penguin
// variant test - use `is`, NOT ==
if (opt is Option<i32>.some) { ... }
if (def is BoundDefinition.function_def) { ... }

// comparing two enum variables: cast both to string
if (cast<string>(a) == cast<string>(b)) { ... }
```

### Accessing Enum Payload

```penguin
let a: mut Option<i32> = new Option<i32>.some(42);
if (a.is_some()) {
    let value: i32 = a.some;      // payload via variant name
}
// or with try-bind:
if (let v := a.some) { ... }
```

## Interfaces

```penguin
interface IBook {
    fun get_title() -> string;

    fun get_language(this: IBook) -> string {   // default implementation - allowed
        return "English";
    }
}

class BookA {
    impl IBook {
        fun get_title() -> string { return "A"; }
    }
}

// or outside the class:
impl IBook for BookB {
    fun get_title() -> string { return "B"; }
}

// marker impls (no body):
impl ICopy<i64> for i64;
impl IHash<f64> for f64;
```

### Implementing with a typed `this`

Interface methods that declare `this` are implemented with the interface-typed receiver, then cast to the concrete type:

```penguin
class RangeIterator {
    start: mut i64 = 0;
    end: mut i64 = 0;

    impl IIterator<i64> {
        fun next(this: mut IIterator<i64>) -> Option<i64> {
            let self: mut RangeIterator = cast<mut RangeIterator>(this);
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

## Generics / Templates

Generics are declared with `#template(...)` BEFORE the definition — never `class Foo<T>`:

```penguin
#template(T: type)
class Box {
    value: T = ...;
    fun new(mut this, value: T) {
        this.value = value;
    }
}

#template(T: type, N: u64)          // value parameters are supported too
class FixedArray { ... }

#template(T: type)
fun identity(x: T) -> T { return x; }

// usage:
let box: Box<i32> = new Box<i32>(42);
let y = identity<string>("hi");
```

Mangling: `Foo__i32`, `identity__string`. Generic specialization is automatic and iterative (transitive deps up to 10 iterations).

## Type Casting — `cast<T>()` ONLY

**There is NO `as` operator** (it was removed / never existed in the current grammar):

```penguin
// WRONG:
let b: f32 = a as f32;

// CORRECT:
let b: float = cast<float>(a);       // numeric conversions
let d: i32 = cast<i32>(b);           // explicit narrowing

// mut/type-qualified casts:
let self: mut RangeIterator = cast<mut RangeIterator>(this);
let da: IDescribe = cast<IDescribe>(p);   // boxing a value type into an interface

// unsafe bit-reinterpretation (EmperorPenguin front-end ONLY - not in compiler sources):
let r: i64 = unsafe_cast<i64>(bits);
```

Implicit widening (`i32 -> i64`) is allowed; everything else needs `cast`.

### Type Checking with `is`

```penguin
if (a is i32) { ... }
if (token is TokenType.EOF) { ... }
if (obj is ISyntaxNode) { ... }
```

## Strings

**There is NO string interpolation and NO printf-style formatting.** `println` takes exactly ONE string. Build strings with `+` concatenation and `cast<string>()`:

```penguin
// WRONG - '{}' formatting does not exist:
println("value: {}", x);

// CORRECT:
println("value: " + cast<string>(x));

let sb = new StringBuilder();
sb.append("hello ");
sb.append("world");
let result: string = sb.to_string();
```

### String Built-in Functions (extern, `__builtin` namespace, used without prefix)

| Function | Signature | Description |
|----------|-----------|-------------|
| `string_length` | `(s: string) -> i64` | Returns length of string |
| `string_substring` | `(s: string, start: i64, length: i64) -> string` | Extracts substring |
| `string_char_at` | `(s: string, index: i64) -> string` | Returns single character as string |
| `string_char_code` | `(s: string) -> i64` | ASCII code of first character (-1 for empty) |
| `string_find` | `(s: string, sub: string) -> i64` | First occurrence, -1 if not found |
| `string_find_from` | `(s: string, sub: string, start: i64) -> i64` | Find starting from position |
| `string_to_int` | `(s: string) -> i64` | Parse integer, 0 on failure |
| `string_to_double` | `(s: string) -> double` | Parse float |

### String Comparison Rules

- `==` and `!=` work for string equality
- `>=`, `<=`, `>`, `<` do NOT work on strings (runtime error)
- Use `string_char_code(ch) >= string_char_code("0")` for character range checks

## Containers & Standard Library

### `List<T>` / `Queue<T>` (`_utils` — compiler-side; for user programs pass `EmperorPenguin/src/utils.penguin` as an extra source, or use them directly on the BabyPenguin VM)

```penguin
let list = new List<i32>();
list.push(1);                          // add element
let size: u64 = list.size();           // size() returns u64!
let elem: Option<i32> = list.at(0);   // at() takes u64 index
let popped: Option<i32> = list.pop();  // remove last

// IMPORTANT: List.size() returns u64 - cast for i64 comparison
if (cast<i64>(list.size()) > 5) { ... }

// IMPORTANT: List.at() takes u64 - cast from i64 index
let idx: i64 = 3;
let elem = list.at(cast<u64>(idx)).some;

// for-in works directly (List implements IIterable):
for (let item : i32 in list) { ... }
```

### Module availability (IMPORTANT for tests/user programs)

| Module | Availability |
|---|---|
| `core_builtin.penguin` (`__builtin`, `Option`, `Result`, `StringBuilder`, iterators, `range`) | auto-loaded, ALL compilers |
| `io.penguin` (`std.io` — File, read_line, stdin_lines, fs helpers) | auto-loaded, **EmperorPenguin native pass2+ only** (NOT in BabyPenguin VM) |
| `_utils` (`List`, `Queue`) | everywhere for the compiler; user programs must compile `src/utils.penguin` in (Compile.Args) |
| `std.Array<T,N>`, `std.Vector<T>`, `std.HashMap`, json | **pass3-only** bootstrap-deferred modules — NOT auto-loaded; pass via `Compile.Args` (they use `#sizeof`/`#fun` meta — EmperorPenguin-native only) |

## Namespaces, using, export, extern

```penguin
namespace MyModule {
    let b: mut i64 = 0;      // globals at namespace level are legal
    fun helper() -> i64 { return 1; }
}
MyModule.b = 1;

// nested namespaces (EmperorPenguin front-end; BabyPenguin can't resolve member access through them):
namespace std { namespace io { fun read_all() -> string { ... } } }
let s: string = std.io.read_all();

using helpers;                // single identifier, file/namespace top level (EmperorPenguin only)
export class Foo { ... }      // dynlib export marking (EmperorPenguin only)
```

Top-level definitions outside any namespace live in a per-file anonymous namespace (C++ `static` semantics). `__builtin` is implicitly used everywhere.

### Universal extern→C rule (EmperorPenguin)

An `extern fun` in ANY namespace maps to the C symbol `@<full.dotted.name>` with `.` → `_` (`std.io.file_open` → `std_io_file_open`); a bare top-level extern keeps its literal libc symbol (`extern fun abs` → `@abs`). Namespaced externs must be called QUALIFIED. Unreferenced externs cost nothing.

## Concurrency (user applications only — EmperorPenguin native)

The `event`/`emit`/`on` keywords are **gone**. Broadcast is a first-class `Event<T>` value; subscription is a wait loop. There is no `poll`. (`wait` is the only wakeup primitive — EmperorPenguin native; BabyPenguin VM also supports the model.)

```penguin
// Events (anonymous broadcast)
let clk: mut Event<i32> = new Event<i32>();

fun deep(c: mut Event<i32>) { c.emit(1); }    // free functions can emit

initial {
    while (true) {
        let v: i32 = wait clk;                // parks until an emit
        println(cast<string>(v));
    }
}

// wait forms:
wait 5 tick;                    // duration (simulation ticks)
let v: T = wait ev;             // event with payload
let v: T = wait this.x;         // port/channel
let v: T = wait change(x);      // edge — parks until watched value differs, yields new value
wait;                           // bare: one delta round
wait a == 5;                    // condition (re-evaluated each scheduler round)

// Channels (store-and-forward):
let q: mut Fifo<i64> = new Fifo<i64>(8, new FifoPolicy.backpressure());
// also: LatestChannel<T>, MergeChannel<T>, MultiInput<T>
```

### Modules & ports (RTL-style)

```penguin
class Foo {
    input x: i64;              // input port: read / wait inside the module ONLY
    output y: i64;             // output port: write inside, read outside

    initial {
        while (true) {
            let v: i64 = wait this.x;   // consume one input transaction
            this.y = v;                 // assignment sugar for y.write(v)
        }
    }
}

construct {                     // wiring runs at elaboration, before any initial
    let f1: mut Foo = new Foo();
    let f2: mut Foo = new Foo();
    connect(f1.y, f2.x);        // connect is ONLY legal inside construct blocks
}
```

Topology is statically checked (`error[E_WIRING]`): two sources into one input, unconnected default-less inputs of construct-instantiated modules, etc.

### async / futures

```penguin
fun async_task() -> i32 {
    wait;
    return 1;
}

initial {
    let task: mut IFuture<i32> = async async_task();
    let result: i32 = wait task;
}
```

## Meta Programming (user applications / stdlib only — EmperorPenguin native pass2+ for JIT)

```penguin
// meta function - JIT-executed at compile time
#fun fib(n: u32) -> u32 {
    if (n <= 1) return n;
    return fib(n - 1) + fib(n - 2);
}

initial {
    let x: u32 = #fib(10);              // computed at compile time
    let t: #signed_to_unsigned(i32) = 0;   // meta call in TYPE position
}

// compile-time conditionals - #elif/#else are their OWN keywords (not plain else)
#if (T == i32) {
    return 0;
} #elif (T == i64) {
    return 1;
} #else {
    return 2;
}

// compile-time loops
#for (let i : i32 in range(0, N)) { ... }
#while (cond) { ... }
#break;
#continue;

// conditional interface impls on a generic type (NEWEST):
#specializing Wrapped<T> {
    if (T.is_primitive()) {
        impl IDescribe { fun describe(this) -> string { return "primitive"; } }
    } else {
        impl IDescribe { fun describe(this) -> string { return "complex"; } }
    }
}

// built-in meta functions (NOT keywords):
#typeof(x)   #sizeof(T)   #define   #defined   #option   #error("msg")   #warn("msg")
#compiler()  // proxy: resolve_type, create_expression/create_definition, error/warn, ...
// reflection on `type` values: t.fields(), t.methods(), t.variants(), t.display_name(), t.is_primitive()
```

**`const if` / `const for` DO NOT EXIST** — the compile-time constructs are `#if` / `#for` / `#while`.

---

## EmperorPenguin Compiler Subset (RESTRICTED)

When writing or modifying files under `EmperorPenguin/src/**`, `EmperorPenguin/main.penguin`, or `EmperorPenguin/std/penguin/*.penguin` that are part of the bootstrap chain, you may use ONLY the ANTLR-digestible subset ("plain penguin + `#template`"). Everything else must go through the stub pattern.

### ✅ ALLOWED in compiler sources

- `#template(T: type)` generics — the ONLY `#` directive (with `#template(N: u64)` value params)
- Flat namespaces: `namespace emperor { ... }`, `namespace _utils { ... }` — fully-qualified access, no `using`, no nesting
- Classes: fields with defaults, `fun new(mut this, ...)`, `(this)`/`(mut this)` methods, `impl` blocks inside classes, marker impls (`impl __builtin.ICopy<T> for T;`, `impl __builtin.IReferenceType;`)
- Enums with class payloads + enum methods; `new X.V()` construction; `is` checks
- `cast<T>()` (the only cast)
- `Option<T>` idioms incl. try-bind `if (let x := o.some)`
- for-in over `_utils.List<T>`; `while` loops; `break`/`continue`
- String `+` concatenation, `cast<string>()`, `StringBuilder`
- `let x: mut T` / `let mut x = ...` / `!mut` fields
- `mut Option<T>` back-references to break class-field default-construction cycles (e.g. `model: mut Option<SemanticModel> = new Option<SemanticModel>.none();` — the MetaEngine.owner_model precedent)
- Parallel `_utils.List`s instead of maps (no hashmap in the compiler core)
- `extern fun` ONLY inside `_utils` (file I/O, exec, temp dirs — routed to `_emperor_*` C runtime symbols)

### ❌ FORBIDDEN in compiler sources (ANTLR grammar / BabyPenguin can't digest, or bootstrap-gated)

| Forbidden | Reason | Workaround |
|---|---|---|
| `#fun`, `#if`/`#elif`/`#else`, `#for`, `#while`, `#define`/`#defined`/`#option`, `#typeof`, `#error`, `#specializing`, `#class` | ANTLR grammar has only `#template` | generate code as source STRINGS (see `MetaEngine.penguin`), or plain runtime code |
| `#sizeof` / `#__load` / `#__store` intrinsics | same — EXCEPT `src/utils.penguin`, which is compiled by EmperorPenguin's own parser (not in the pass1 project; BabyPenguin supplies `_utils` via `BabyPenguin/Utils.penguin` extern classes) | put pointer code in `utils.penguin` only |
| Nested namespaces + member access through them (`std.io.x()`) | BabyPenguin can't resolve nested-ns member access | flat namespaces, fully-qualified names |
| `using`, `export`, `unsafe_cast` | EP front-end only | fully-qualified names; `cast<T>()` |
| `wait`, `async`, `yield`, `Event<T>`, channels, `input`/`output`/`connect`/`construct` | compiler is wait-free; coroutine emission is `--enable-coroutine`-gated (pass3+) | plain sequential code |
| `try`/`catch`/`panic` | not used in bootstrap sources | error enums / `Result`-style returns |
| Lambdas / function values | NEWLY LIFTED (fun-values branch): EP pass2+ compiles lambdas, `fun<...>`/`async_fun<...>` types, method refs — but compiler sources still avoid them (conservative; the whole bootstrap chain must stay in the safe subset) | plain `fun`s (namespace-level) |
| `Self` type keyword | grammar supports it, convention avoids it | `let self = cast<ConcreteType>(this);` |
| `auto` mutability specifier | not used | explicit mutability |
| `std.io` (`io.penguin`) | pass2+ native only — auto-loaded by main.penguin but NOT implemented in BabyPenguin VM | `_utils` file externs |
| `std.Array`/`Vector`/`HashMap`/json modules | pass3-only, and they USE meta (`#fun`/`#sizeof`) — would never parse at pass1 | `_utils.List` parallel lists |
| Type aliases (`type X = ...`) | not used in compiler sources | — |

The compiler's own style rules are stated in its sources: "no meta/#option anywhere in the compiler's own code" (`src/project/CompilerConfig.penguin`) and `std/penguin/dynlib.penguin`'s header: "No meta anywhere in this module: the compiler's own code is plain penguin."

### The stub mechanism

The pass1 project (`EmperorPenguinPass1.penguins`) swaps in stubs where the real module needs ANTLR-unsafe features:

- `src/meta/MetaConfigStub.penguin` — `meta_runtime_available() -> false`; replaced by `std/penguin/metaconfig.penguin` (returns `true`, enables the JIT) in `EmperorPenguinPass2.penguins`
- `src/project/DynlibStub.penguin` — mirrors the public API of the json-backed `std/penguin/dynlib.penguin`; stubbed functions error with "dynamic linking disabled", `dynlib_available() -> false` lets `main.penguin` report cleanly. **When you add a function to the real Dynlib, mirror it in the stub** (and keep the stub ANTLR-safe)
- `src/utils.penguin` — the ONE file allowed `#sizeof`/`#__load`/`#__store`; not listed in the pass1 project (the Makefile hands it to the already-running EmperorPenguin as a runtime arg). At pass1, `_utils.List/Queue` come from `BabyPenguin/Utils.penguin` (extern classes implemented by the C# VM)

When adding a new source file to the compiler:
- `src/bound/*.penguin` → add to **BOTH** `EmperorPenguinPass1.penguins` AND `EmperorPenguinPass2.penguins` (+ `EmperorPenguinLib.penguins` if it defines bound-tree code)
- the file must be ANTLR-safe unless it rides one of the bridges above
- source style: see `src/bound/SemanticModel.penguin`, `src/ast/AST.penguin` for the idioms (pass classes with `model: mut Option<SemanticModel>` back-reference, single `run()` entry, original method names for `catch_up_def` replay)

---

## Common Pitfalls

1. **`new` is required for enum variants**: `new TokenType.EOF()` not `TokenType.EOF`
2. **`is` not `==` for enum check**: `x is TokenType.EOF` not `x == TokenType.EOF`; compare two enum variables via `cast<string>()` equality
3. **No `as` operator**: always `cast<T>(expr)`
4. **No `let mut x: Type`**: use `let x: mut Type = value` or `let mut x = value`
5. **No string interpolation / printf formatting**: `println("x=" + cast<string>(x))`, never `println("{}", x)`
6. **`const if`/`const for` don't exist**: the meta constructs are `#if`/`#for`/`#while`
7. **`List.size()` returns `u64`**: need `cast<i64>()` for i64 arithmetic/comparison
8. **`List.at()` takes `u64`**: need `cast<u64>()` when using i64 index
9. **No string relational operators**: use `string_char_code()` for character range checks
10. **Enum constructor params need `mut`**: `fun new(mut this, e: mut MyEnum)`
11. **All class fields need default values**: `field: i32 = 0;`
12. **Generics are `#template(T: type)` before the def**, never `class Foo<T>`
13. **`some`/`none` are Option methods**: `opt.is_some()`, `opt.is_none()`, `opt.some`, `opt.value_or(d)`
14. **Cast interface this to concrete type**: `let self: mut X = cast<mut X>(this);`
15. **Compiler sources: ANTLR-safe only** — no meta, no nested namespaces, no `using`/`export`/`unsafe_cast`, no concurrency, no try/catch, no lambdas
16. **`std.io` and the pass3 stdlib modules are NOT on BabyPenguin**: io tests are Pass2/Pass3-only; `std.Array/Vector/HashMap/json` need Compile.Args and pass3

## Keyword Collision Avoidance (CRITICAL)

PenguinLang has many reserved keywords. When naming enum variants, you MUST avoid these words — the parser rejects any identifier that matches a keyword.

**Complete keyword list** (from the EmperorPenguin lexer; integer type names `i8`…`u64` are also reserved type names):
`async`, `async_fun`, `auto`, `bool`, `break`, `cast`, `catch`, `char`, `class`, `connect`, `construct`, `continue`, `double`, `elif`, `else`, `enum`, `export`, `extern`, `false`, `float`, `for`, `fun`, `if`, `impl`, `in`, `initial`, `input`, `interface`, `is`, `let`, `mut`, `namespace`, `new`, `output`, `pure`, `return`, `string`, `template` (after `#`), `this`, `tick`, `true`, `try`, `type`, `unsafe_cast`, `using`, `void`, `wait`, `while`, `yield`, `Self`

**Strategy**: add a suffix to disambiguate:

```penguin
// BAD - keywords as variant names (parse errors):
enum PrimitiveType { Bool; String; Void; Char; Float; }
enum TypeKind { Class; Enum; Interface; Function; }

// GOOD:
enum PrimitiveType { BoolKind; StringKind; VoidKind; CharKind; FloatKind; }
enum TypeKind { ClassKind; EnumKind; InterfaceKind; FunctionKind; }
enum BoundSymbol { type_sym; function_sym; namespace_sym; }
```

**Existing codebase convention** (see `EmperorPenguin/src/ast/Token.penguin`): `StringKw` for the `string` keyword token, `SelfKw` for `self`, `ThisKw` for `this`, `EventKw` for `event`, `TypeKw` for `type`.

**Rule of thumb**: when in doubt, add `Kind`, `Kw`, `Sym`, or `Def`. Check `EmperorPenguin/src/ast/Token.penguin` for the full list of token naming conventions.

## Mutability Patterns for Complex Objects

- **PenguinLang mutability model**: three field modes — `mut T` (always mutable), `T` (auto: follows instance mutability), `!mut T` (always immutable)
- **Auto-mutability limitation**: no auto-mutable `List<T>` fields in `mut this` methods — `push()` fails. Give `List<T>` fields explicit `mut`
- **Immutable-to-mutable assignment**: `mut T` fields/locals can't accept immutable `T` values (same for `mut Option<T>`). Functions feeding `mut` fields must return `-> mut T`
- **Simple type copy semantics**: `string` assigns to auto-mutable fields without `mut` on the param; `bool` and enums need `mut` on constructor params for auto-mutable fields
- **Early return pattern**: instead of `let x: mut ComplexType = default; if (c) { x = v; }`, restructure with early returns to avoid mutable complex-type locals
- **Constructor pattern**: expression/statement classes use `!mut` fields + constructors (set once); definition/symbol/scope classes use auto-mutable fields set through `mut` bindings
- **Cycle breaking**: `model: mut Option<SemanticModel> = new Option<SemanticModel>.none();` back-references (class-field default construction can't reference the class being defined)

## Naming Conventions

### Avoid `_static` suffix
```penguin
// BAD
fun from_sexp_static(sexp: string) -> mut SourceLocation { ... }
// GOOD
fun from_sexp(sexp: string) -> mut SourceLocation { ... }
```

### Avoid Utils classes
Place utility functions at namespace level, not in `XXXUtils` classes:
```penguin
// BAD
class SexpUtils { fun escape_string_static(s: string) -> string { ... } }

// GOOD
namespace ast {
    fun escape_string(s: string) -> string { ... }
}
```

## Using `Option<T>` Effectively

```penguin
class PrimaryExpression {
    identifier: mut Option<SymbolIdentifier> = new Option<SymbolIdentifier>.none();
    literal_value: string = "";

    fun to_sexp(this) -> string {
        if (this.identifier.is_some()) {
            return this.identifier.some.to_sexp();
        } else {
            return this.literal_value;
        }
    }
}
```

Prefer try-bind `if (let v := opt.some)` for local payload extraction; prefer `is_some()`/`.some` chains inside methods.

## Best Practices

1. Use `initial` for entry points, not `main`
2. Prefer immutability by default; put `mut` on the type (`let x: mut T`), not on `let` with an annotation
3. Use `Option<T>` instead of null; `Result<T,E>` or error enums instead of exceptions
4. Use `Event<T>` values + wait loops and channels for cross-routine communication (NOT removed `event`/`emit`/`on` keywords)
5. Leverage `async`/`wait` for concurrent operations (user applications only)
6. Use `#fun`/`#if`/`#specializing` meta for compile-time work (user applications, EmperorPenguin native)
7. File extension: `.penguin`
8. **Avoid `_static` suffix** — use simple function names; **avoid `XXXUtils` classes** — namespace-level functions
9. **Interface methods with `this` go in `impl` blocks**, typed as the interface, cast to concrete inside
10. **When writing compiler sources, re-check the restricted-subset table above before using ANY feature newer than plain penguin + `#template`**
11. **Read `Tests/<Category>/*.md` first** — copy the style of an existing test in the relevant category; it is the ground truth for current syntax
12. **Persist bugs as markdown tests** under `Tests/<Category>/<Name>.md` (format in `Tests/Readme.md`) — assert the correct intended behavior even for unfixed bugs (red sentinel on the buggy compiler, green on the reference)
