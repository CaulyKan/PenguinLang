# Meta Programming

Penguin-lang provides powerful compile-time meta-programming capabilities through **Meta Functions** and **Compile-Time Evaluation**. This allows you to write code that executes during compilation, enabling zero-cost abstractions and type-level computations.

> **Status (2026-08-01).** This document describes the target design. **Reflection Round 1 shipped (2026-07-29)** as an interim — opaque type-tokens + per-op host callbacks (`#field_count(t)` etc.); **Phase 6 v2** (real-pointer reuse: `type = BoundType`, `t.fields()` direct; per-call-site caller-stub `#fun` ABI; `#class`) is the current implementation direction — see `.claude/plans/meta_plan.md` §0.4. Where this doc shows `t.fields()` / `t.methods()` / `t.variants()` (the v2 form), Round 1 used the procedural `#field_count(t)` / `#field_name(t,i)` equivalents; v2 replaces them.
>
> **v1 surface:** `#fun`, `#if` / `#elif` / `#else`, `#for` / `#while` / `#break` / `#continue`, `#template`, `#typeof`, `#compiler`, `#define` / `#defined` / `#option`, `#class` (meta-only data structures — see [Meta Classes](#meta-classes-class)), and `cast<T>()` (there is **no** `#cast` — it was a typo for the existing `cast<T>()`).
>
> **Reflection API — defined (reuse-based, see [Reflection API](#reflection-api) below).** `type` carries reflection methods (`t.fields()` / `t.methods()` / `t.variants()` / `t.display_name()` / `t.is_class()` / …); the objects those return are **the compiler's own bound types** surfaced under the aliases `Field` / `Method` / `Variant` / `Param` / `Symbol`; `compiler()` exposes context operations (`resolve_type`, `create_ast`, `can_compile_expression`, `error`, options). Meta code and the compiler read the **same live objects** — there is no parallel `FieldInfo`/`MethodInfo` hierarchy.
>
> **Deferred to a later version (marked `[v2]` where they appear):** `Map`, collection-iteration `#for (let x : coll)`, type-value method chains such as `#typeof(T).as_enum().enum_items()`, and annotations / `get_methods_with_attribute` (PenguinLang has **no annotation system** — do attribute-like generation with `#fun + ast`, passing a description explicitly).
>
> **`#` is the meta-space access prefix.** `#fun()` calls a meta function; `#item` reads a meta-space variable (e.g. a `#for` loop variable); `.` performs member access on the result. (The precise rules for when `#` is optional *inside* meta context are still being finalized.)

## Overview

| Feature                | Syntax             | Description                                            |
| ---------------------- | ------------------ | ------------------------------------------------------ |
| Meta Function          | `#fun`             | Function executed at compile time via JIT              |
| Meta Function Call     | `#func_name()`     | Invoke a meta function                                 |
| Compile-Time Condition | `#if` / `#elif` / `#else` | Conditional code generation                     |
| Compile-Time Loop      | `#for` / `#while` / `#break` / `#continue` | Loop unrolling at compile time               |
| Generic Declaration    | `#template`        | Syntactic sugar for type-level meta functions          |
| Compile-Time Options   | `#define` / `#defined` / `#option` | Built-in meta functions; compile-time key-value store |
| Compiler Interface     | `#compiler()`      | Built-in meta function; access compiler internals     |
| Type Query             | `#typeof(T)`       | Built-in meta function; resolve type at compile time  |

### Execution Model

PenguinLang meta programming executes at **compile time** via LLVM ORC JIT: the compiler compiles each `#fun` to native code in memory, calls it, and splices the result (a value, a type, or an AST fragment) back into the compilation pipeline. This gives meta functions the full performance of native code while maintaining seamless access to compiler internals.

Meta calls are executed at different pipeline stages depending on where they appear:

| Location                          | Execution Pass          |
| --------------------------------- | ----------------------- |
| Global / namespace scope          | `pass_build_scopes`     |
| Type positions (return type etc.) | `pass_resolve_types`    |
| Inside class / enum / interface   | `pass_bind_symbols`     |
| Inside function / routine body    | `pass_bind_expressions` |
| Inside `#template` body           | `pass_monomorphize` (at instantiation time) |

## Meta Functions

A meta function is a function prefixed with `#` that executes during compilation. It must have an explicit return type and can return a value, a type, or an AST fragment.

### Basic Meta Function

```penguin
#fun fib(n: u32) -> u32 {
    // Regular Penguin syntax, executed at compile time via JIT
    if (n <= 1) return n;
    return fib(n-1) + fib(n-2);  // Recursive calls don't need # prefix
}

initial {
    let x: u32 = #fib(10);  // x = 55, computed at compile time
    // The above is equivalent to: let x: u32 = 55;
}
```

### Meta Function Declaration Rules

- `#fun` is only allowed at **global scope** or **namespace scope**
- Must have an explicit return type annotation (`-> u32`, `-> type`, `-> ast`)
- Parameters are typed: you can use `type`, `ast`, or any primitive type
- If the **last** parameter is `ast`, a trailing `{ }` code block after the call site is bound to it
- Recursive calls within the body do **not** need the `#` prefix
- Meta functions are compiled on-demand and cached after first use

### Type-Level Meta Function

Meta functions can operate on and return types. The `type` parameter/return type represents a compile-time type reference:

```penguin
#fun signed_to_unsigned(t: type) -> type {
    if (t == #typeof(i32)) return #typeof(u32);
    if (t == #typeof(i64)) return #typeof(u64);
    if (t == #typeof(i8))  return #typeof(u8);
    if (t == #typeof(i16)) return #typeof(u16);
    compiler().error("Unsupported type");
}

initial {
    let t : #signed_to_unsigned(i32) = 0;  // t is u32
}
```

This approach provides a straightforward way to manipulate types, avoiding the complex template metaprogramming techniques required in C++.

### Using Meta Functions in Type Positions

```penguin
#template(T: type)
fun abs(v: T) -> #signed_to_unsigned(T) {
    if (v > 0) {
        return cast<#signed_to_unsigned(T)>(v);
    } 
    else {
        return cast<#signed_to_unsigned(T)>(-v);
    }
}
```

## Meta Classes: `#class`

`#class` declares a **meta-only** data structure — a class that exists only in the meta compilation (unit B), usable by `#fun`s to hold compile-time intermediate data, and **not emitted to the runtime program**. It is the meta-space counterpart of `#fun`, and (like `#fun`) is declared in unit A source and routed into unit B.

`#class` has **full class capabilities** — fields, methods, generics, mutability — compiled as a normal class inside unit B. The `#` prefix carries only two responsibilities: mark it meta-only and route it into unit B.

```penguin
#class FieldSpec {
    name: string;
    type_name: string;
    fun new(mut this, name: string, type_name: string) {
        this.name = name;
        this.type_name = type_name;
    }
    fun render(this) -> string {
        return this.name + ": " + this.type_name;
    }
}

#fun summarize(t: type) -> string {
    // Build a list of #class instances from the type's fields, then serialize.
    let fs = t.fields();
    let mut acc = new StringBuilder();
    let i = 0;
    while (i < cast<i64>(fs.size())) {
        let f = fs.at(cast<u64>(i)).some;
        let spec = new FieldSpec(f.name, f.bound_type.display_name());
        acc.append(spec.render());
        acc.append("; ");
        i = i + 1;
    }
    return acc.to_string();
}
```

**Rules:**
- `#class` is allowed wherever `#fun` is (global / namespace / class-member scope).
- It is **meta-only**: instances cannot be returned from a `#fun` into runtime code (a `#fun`'s return type must be a runtime-compatible type — `i64`/`bool`/`type`/`ast`/`string`). Use `#class` for compile-time bookkeeping inside the meta compilation.
- It may use the reflection types directly (`BoundType` via `type`, `BoundClassFieldDefinition` via `t.fields().at(i).some`, etc.) since unit B compiles the real bound types.

## Built-in Meta Functions

PenguinLang provides several **built-in meta functions** that are automatically available without being declared. They use the standard meta function call syntax (`#name(args)`) — they are NOT separate parser keywords. Users can shadow them by defining a `#fun` of the same name.

### `#typeof(T)` — Type Query

`#typeof(T)` resolves a type name to a `type` value at compile time. It works in both meta and non-meta contexts with unified semantics:

- **In non-meta context** (global scope, function bodies): `#typeof(i32)` is resolved during the host compiler's pipeline; the result is a `BoundType*` constant.
- **In meta function bodies**: `#typeof(i32)` is resolved when the meta function is **JIT-compiled**; by that point all type names in scope are known and the result is embedded as a constant in the generated native code.

```penguin
#fun make_optional(t: type) -> type {
    return #typeof(Option) with [t];  // Option<T>
}

initial {
    let a: #typeof(i32) = 42;                     // direct type query
    let b: #make_optional(i32) = Option.some(1);  // type computed by meta function
}
```

### Dynamic Type Resolution

For cases where the type name is computed at meta-execution time (e.g., from a string), use `compiler().resolve_type(name)`:

```penguin
#fun lookup_type(name: string) -> Option<type> {
    return compiler().resolve_type(name);   # None if the name is unknown
}
```

## Meta Call Syntax

The general syntax for calling a meta function or built-in meta construct is:

```
'#' identifier ('(' argument_list ')')? (';' | trailing_block)
```

**Rules:**
- If the meta function's last parameter is `ast`, a trailing code block or definition is bound to that parameter as a structured AST node
- If no trailing block, the call must end with `;`
- A standalone `#name` (without `()`) followed by a definition block is equivalent to `#name() { definition }`

```penguin
#getter() { x };             // trailing code_block → ast param
#derive_clone(Point);        // no trailing block → must end with ;
#test                         // standalone → equivalent to #test() { class A {} }
class A {}
```

## Compile-Time Conditions: `#if`

`#if` enables conditional code generation at compile time. The condition must be evaluable at compile time. All branches are compile-time; you cannot mix runtime and compile-time branches.

```penguin
#template(T: type)
fun default_value() -> T {
    #if (T == i32) {
        return 0;
    } #elif (T == f32) {
        return 0.0;
    } #elif (T == string) {
        return "";
    } #else {
        return T.default();
    }
}
```

**Important**: `#if` is a hardcoded compiler construct (not a user-defined `#fun`). Its condition is evaluated directly by the compiler in the host process, and only the selected branch survives to subsequent compilation passes. This is more efficient than JIT-compiling an `if` function and avoids bootstrap circularity.

> **Branch tokens are `#`-prefixed.** `#elif` and `#else` are parser keywords, **not** plain `else` / `else if`. A plain `else if` after `#if { … }` would parse as a *runtime* `if` nested inside the compile-time else branch — a different semantics. Always write `#elif` / `#else`.

```penguin
// CORRECT: All branches are compile-time
#if (condition) {
    // ...
} #else {
    // This is also compile-time
}

// CORRECT: #if at global scope controls entire definitions
#if (option("PLATFORM") == "linux") {
    extern fun linux_specific() -> i32;
} #else {
    extern fun generic_impl() -> i32;
}
```

## Compile-Time Loops: `#for` and `#while`

`#for` enables loop unrolling at compile time. The loop variable range must be compile-time constants:

```penguin
#template(N: u32)
fun sum() -> u32 {
    let result: u32 = 0;
    #for (i in range(0, N)) {
        result = result + i;
    }
    return result;
}

initial {
    let x = sum<10>();  // Compile-time equivalent of:
                        // let result = 0;
                        // result = result + 0;
                        // result = result + 1;
                        // ... (unrolled 10 times)
}
```

`#while` similarly evaluates its condition at compile time to determine how many times to unroll:

```penguin
#fun count_bits(v: u32) -> u32 {
    let mut bits: u32 = 0;
    let mut remaining: u32 = v;
    #while (remaining > 0) {
        bits = bits + (remaining & 1);
        remaining = remaining >> 1;
    }
    return bits;
}
```

**Implementation note**: `#for` and `#while` are hardcoded compiler constructs (same as `#if`), not JIT-compiled. The compiler evaluates loop bounds and conditions directly, unrolling the body accordingly. This avoids the overhead of JIT for simple compile-time iteration.

> **`#for` grammar.** `#for` reuses the standard (non-`#`) for-grammar, prefixed with `#` — e.g. `#for (i in 0..N) { … }`. The loop variable lives in meta space and is read as `#i`. A collection-iteration form `#for (let x : coll)` is planned but **deferred `[v2]`**.

**Compile-time loop control — `#break` / `#continue`.** Inside a meta-loop body, plain `break` / `continue` are *runtime* (emitted into the unrolled code, e.g. inside a runtime loop in the body). `#break` / `#continue` are *compile-time* — they abort or skip the current unrolling iteration. They are `#`-prefixed parser keywords for the same reason as `#else` / `#elif` (to keep compile-time control flow unambiguous and visible), and are rarely needed.

```penguin
#fun first_matching(start: u32, end: u32) -> u32 {
    #for (i in start..end) {
        #if (is_prime(i)) {
            return i;          // runtime return, emitted in the unrolled body
        }
        // (no #break here — the loop fully unrolls)
    }
    return 0;
}
```

## Generic Declaration: `#template`

`#template` is syntactic sugar for a meta function that returns a type. Conceptually, the compiler transforms `#template` declarations into equivalent `#fun` forms internally:

```penguin
// These two declarations are conceptually equivalent:

// Style 1: #template sugar (user-facing)
#template(T: type)
class Box<T> {
    value: T;
}

// Style 2: Semantic equivalent (compiler internal)
// #fun Box(T: type) -> type {
//     return compiler().create_class(...);
// }
```

### Template Parameters

Templates support both type and value parameters:

```penguin
#template(T: type, default_value: T)
class Container<T> {
    data: T = default_value;
}
```

### `#define(key, value)` / `#defined(key)` / `#option(key)` — Compile-Time Options

These built-in meta functions provide a lightweight compile-time key-value store, which can also be set in compiler command line(`-DFoo=Bar`). They delegate to `compiler().set_option()`, `compiler().get_option()`, and `compiler().has_option()` respectively:

```penguin
initial {
    #define("PI", 3.14);
    println("PI = {}", #option("PI"));          // PI = 3.14
    #if (defined("PI")) {
        println("PI is defined");
    }
}
```

### `#compiler()` — Compiler Context Access

`#compiler()` returns the current compiler context (an `ICompiler`), giving meta functions access to **context operations**: type/symbol resolution, text→AST parsing, expression probing, diagnostics, and the compile-time option store. **Structural reflection is not on `compiler()` — it lives on `type`** (see [Reflection API](#reflection-api) below).

**`ICompiler` methods:**

| Method | Signature | Description |
|---|---|---|
| `resolve_type` | `(name: string) -> Option<type>` | Resolve a (qualified) type name; `None` if unknown |
| `resolve_symbol` | `(name: string) -> Option<Symbol>` | Look up a symbol; `None` if unknown |
| `has_type` | `(name: string) -> bool` | Whether a type name is visible |
| `create_ast` | `(code: string) -> ast` | Parse source text into a structured AST node |
| `create_empty_ast` | `() -> ast` | Return an empty no-op AST |
| `create_function_ast` | `(name: string, return_type: type, body: string) -> ast` | Build a function AST |
| `can_compile_expression` | `(expr: string) -> bool` | Probe whether an expression type-checks (side-effect-free; see [Probing Type Capabilities](#probing-type-capabilities)) |
| `error` / `warn` | `(msg: string)` | Emit a diagnostic at the current source location |
| `set_option` / `get_option` / `has_option` | `(key, value?)` | Compile-time key-value option store (also exposed as `#define` / `#option` / `#defined`) |

### Reflection API

Reflection reads the compiler's **own bound types directly** — there is no separate `FieldInfo` / `MethodInfo` hierarchy. `type` *is* the compiler's `BoundType`; the objects returned by `t.fields()` / `t.methods()` / `t.variants()` are the compiler's `BoundClassFieldDefinition` / `BoundFunctionDefinition` / `BoundEnumMemberDefinition`, surfaced under short aliases. Meta code and the compiler manipulate the **same live objects** (read-only views over current bound state).

**Aliases** (declared in the meta runtime stdlib):

| Meta name | Compiler type it reuses |
|---|---|
| `type` | `BoundType` |
| `Field` | `BoundClassFieldDefinition` |
| `Method` | `BoundFunctionDefinition` |
| `Param` | `BoundFunctionParameter` |
| `Variant` | `BoundEnumMemberDefinition` |
| `Symbol` | `BoundSymbol` |
| `ast` | AST `Expression` / `Statement` / `Definition` |

**`type` methods** (the structural reflection surface):

| Method | Description |
|---|---|
| `display_name() -> string` | Full source spelling, e.g. `Option<i32>` (alias: `to_string()`) |
| `kind() -> TypeKind` | `Primitive` / `Class` / `Enum` / `Interface` / `Function` / `TypeReference` / `Error` |
| `is_class()` / `is_enum()` / `is_interface()` / `is_primitive()` | Kind predicates |
| `is_value_type()` / `is_reference_type()` | ICopy (stack) vs IRef (heap) classification |
| `fields() -> List<Field>` | Class fields (empty for non-class) |
| `methods() -> List<Method>` | Class / interface methods |
| `variants() -> List<Variant>` | Enum variants (empty for non-enum) |
| `generic_args() -> List<type>` | Instantiated generic arguments, e.g. `Option<i32>` → `[i32]` |

The returned `Field` / `Method` / `Variant` objects expose their data as ordinary fields — read them directly: `f.name`, `f.bound_type`, `m.parameters`, `m.return_type`, `m.signature()`, `v.name`, `v.value`.

```penguin
#fun derive_clone(t: type) -> ast {
    let fs = t.fields();                              # List<Field>
    let mut body = "return new " + t.display_name() + "(";
    let i = 0;
    #while (i < fs.size()) {
        #if (i > 0) { body = body + ", "; }
        body = body + "this." + fs.at(i).some.name;   # read Field.name directly
        i = i + 1;
    }
    body = body + ");";
    return compiler().create_function_ast("clone", t, body);
}
```

> **No annotation system.** PenguinLang has no `@attr` syntax. Patterns like "scan methods for an annotation" are done with `#fun + ast`: the caller passes a description explicitly (e.g. a `List` of transition specs), and the meta function generates code from it. There is no `get_methods_with_attribute` / `get_attribute` API.

> **Design note**: `#compiler()`, `#typeof(T)`, `#define(key,val)`, `#defined(key)`, and `#option(key)` are **built-in meta functions** — not parser keywords. The compiler provides their implementations directly (no JIT needed). They use the exact same `#identifier(args)` syntax as user-defined meta functions, and users can shadow them by defining a `#fun` of the same name. In the future `#compiler()`'s return value may be abstracted behind an `interface ICompiler` for API stability.

### Probing Type Capabilities

The `can_compile_expression` method allows meta functions to check whether a type supports a particular operation **without side effects** — it probes only expression-level compilation, which is safe to run inside JIT'd code:

```penguin
#template(T: type)
fun try_call_foo(v: T) {
    #if (compiler().can_compile_expression("v.foo()")) {
        v.foo();  // Only generated if T has method foo()
    } #else {
        print("Type does not have foo() method");
    }
}
```

> **Current limitation**: `can_compile_expression` is restricted to expression probes to avoid re-entrant compiler state modification. Full `can_compile` (allowing statements and definitions) may be added once the compiler is proven re-entrant-safe.

### Building Custom Constraints

You can build custom constraint functions similar to C++ concepts:

```penguin
#fun Addable(t: type) -> bool {
    return compiler().can_compile_expression("let a: " + t.display_name() + "; let b: " + t.display_name() + "; a + b;");
}

#fun Comparable(t: type) -> bool {
    return compiler().can_compile_expression("let a: " + t.display_name() + "; let b: " + t.display_name() + "; a < b;");
}

#template(T: type)
fun max(a: T, b: T) -> T {
    #if (Comparable(T)) {
        if (a < b) return b;
        return a;
    } else {
        compiler().error("Type T must be Comparable");
    }
}
```

## AST Parameters and Code Generation

### AST Type Parameter

When a meta function's last parameter is of type `ast`, the trailing code block at the call site is passed as a structured AST node rather than being evaluated:

```penguin
#fun getter(field: ast) -> ast {
    // field contains the structured AST of the trailing { } block
    let field_name = field.as_identifier();
    let field_type = compiler().resolve_symbol(field_name).get_bound_type();

    return compiler().create_function_ast(
        "get_" + field_name,
        field_type,
        "return this." + field_name + ";"
    );
}

class Point {
    x: i32;
    y: i32;

    #getter { x };  // Generates: fun get_x() -> i32 { return this.x; }
    #getter { y };  // Generates: fun get_y() -> i32 { return this.y; }
}
```

The `ast` type represents structured AST nodes in the compiler's internal representation — not raw text. This enables seamless integration with subsequent compiler passes (type resolution, symbol binding) without re-parsing. For convenience, `compiler().create_ast(code_string)` provides text-to-AST conversion within meta functions.

### Conceptual Model: `#if` as a Meta Function

You can think of `#if` as conceptually equivalent to a built-in meta function:

```
#fun if(cond: bool, body: ast) -> ast {
    if (cond) {
        return body;
    } else {
        return compiler().create_empty_ast();
    }
}
```

In practice, `#if` is implemented as a hardcoded compiler construct for efficiency, but the mental model is the same: a compile-time function that chooses which AST to keep.

### Variadic Support via AST

A meta function with a single `ast` parameter and no other parameters can receive a trailing `{ }` block containing comma-separated expressions as variadic arguments:

```penguin
#fun repeat_each(content: ast) -> ast {
    // content.as_expressions() returns the list of comma-separated expressions
    let exprs = content.as_expressions();
    let mut code = "";
    let i: u64 = 0;
    while (i < exprs.size()) {
        code = code + "print(" + exprs.at(i).some.to_string() + ");\n";
        i = i + 1;
    }
    return compiler().create_ast(code);
}

initial {
    #repeat_each { "hello", 42, 3.14 };
    // Generates:
    //   print("hello");
    //   print(42);
    //   print(3.14);
}
```

### Meta Functions Returning AST

When a `#fun` returns `ast`, the result is spliced into the source at the call site and continues through the normal compilation pipeline:

```penguin
#fun derive_clone(t: type) -> ast {
    let fields = t.fields();
    let mut clone_body = "return new " + t.to_string() + "(";

    let i: u64 = 0;
    while (i < fields.size()) {
        if (i > 0) { clone_body = clone_body + ", "; }
        clone_body = clone_body + "this." + fields.at(i).some.name;
        i = i + 1;
    }
    clone_body = clone_body + ");";

    return compiler().create_function_ast(
        "clone",
        t,
        "fun clone(this) -> " + t.to_string() + " { " + clone_body + " }"
    );
}

class Point {
    x: i32;
    y: i32;

    #derive_clone(#typeof(Self));  // Inserts clone() method implementation
}
```

---

## More Examples

> Each example below demonstrates a real-world engineering problem solved with PenguinLang meta programming, with parallel implementations in C++ and TypeScript for comparison.

### Example 1: Enum ↔ String Bidirectional Mapping

**Problem**: Given an enum, automatically generate `to_string()` and `from_string()` without manual maintenance. Adding a new variant should require zero changes to the mapping code.

**PenguinLang**

> ⚠️ **`[v2]`** — the `enum_to_string` meta function below uses collection-iteration `#for`, a type-value method chain (`#typeof(T).as_enum().enum_items()`), and `#item`, all of which are deferred. A v1 equivalent would use `#while` + an index + `compiler().get_enum_variants(t).at(i)`. (`#cast` was a typo for the existing `cast<int>()`.)

```penguin
enum Color {
    Red;
    Green;
    Blue;
}

#template<T: Type>
fun enum_to_string(v: T) -> string {
    #for (let item : #typeof(T).as_enum().enum_items()) {
        if (#item.value == cast<int>(v)) {
            return #item.name;
        }
    }
}

initial {
    let c = new Color.Red();
    println(enum_to_string(c));                     // "Red"
}
```

**C++** (using `magic_enum` — a third-party library)

```cpp
#include <magic_enum.hpp>

enum class Color { Red, Green, Blue };

int main() {
    Color c = Color::Red;
    std::cout << magic_enum::enum_name(c) << "\n";            // "Red"

    auto parsed = magic_enum::enum_cast<Color>("Blue");
    if (parsed.has_value()) {
        std::cout << magic_enum::enum_name(parsed.value());   // "Blue"
    }
}
```

> **C++ analysis**: `magic_enum` works by parsing `__PRETTY_FUNCTION__` / `__FUNCSIG__` compiler builtins to extract enum value names at compile time. This is clever but fragile — it depends on compiler-specific output formats, requires `__cplusplus >= 201703L`, and is limited to enums with contiguous values. In contrast, PenguinLang's approach uses the compiler's own reflection API and works for any enum definition.

**TypeScript**

```typescript
enum Color { Red, Green, Blue }

// Native reverse mapping (only for numeric enums):
console.log(Color[0]);               // "Red"
console.log(Color["Red"]);           // 0

// For string enums — must write manually or use a helper:
function enumToString<T extends string>(e: Record<string, T>, value: T): string {
    return value;
}
function enumFromString<T extends string>(e: Record<string, T>, str: string): T | undefined {
    return Object.values(e).find(v => v === str) as T | undefined;
}
```

> **TypeScript analysis**: Numeric enums get automatic reverse mapping, but string enums (which are the idiomatic choice) require runtime helpers. There is no way to iterate over enum variant names at compile time — the information exists only in the type system, not in the value space. PenguinLang's meta function can iterate over enum variants because the compiler exposes them as a structured API.

---

### Example 2: Type-Safe Builder Pattern

**Problem**: Given a class with multiple fields, generate a builder that enforces **all required fields** are set before `build()` can be called. Missing a field should be a compile-time error.

**PenguinLang**

```penguin
#fun derive_builder(t: type) -> ast {
    let fields = t.fields();
    let class_name = t.to_string();
    let builder_name = class_name + "Builder";

    // Generate builder fields (wrapping each in Option)
    let mut builder_fields = "";
    let mut setter_methods = "";
    let mut build_checks = "";
    let mut build_args = "";
    let i: u64 = 0;
    while (i < fields.size()) {
        let f = fields.at(i).some;
        builder_fields = builder_fields + "    _" + f.name + ": Option<" + f.bound_type.display_name() + "> = Option.none();\n";

        setter_methods = setter_methods +
            "    fun " + f.name + "(mut this, value: " + f.bound_type.display_name() + ") -> mut " + builder_name + " {\n" +
            "        this._" + f.name + " = Option.some(value);\n" +
            "        return this;\n" +
            "    }\n";

        i = i + 1;
    }

    return compiler().create_ast(
        "class " + builder_name + " {\n" +
        builder_fields + "\n" +
        setter_methods + "\n" +
        "    fun build(this) -> " + class_name + " {\n" +
        "        // At runtime: validate all fields are set\n" +
        "        return " + class_name + " { ";
    );
}

class Person {
    name: string;
    age: u32;
    email: string;
}

#derive_builder(#typeof(Person));

initial {
    let person = PersonBuilder()
        .name("Alice")
        .age(30)
        .email("alice@example.com")
        .build();
    println(person.name);  // "Alice"
}
```

**C++** (Named Parameter Idiom / Builder)

```cpp
#include <string>
#include <iostream>
#include <optional>

class Person {
public:
    std::string name;
    uint32_t age;
    std::string email;
};

class PersonBuilder {
    std::optional<std::string> _name;
    std::optional<uint32_t> _age;
    std::optional<std::string> _email;
public:
    PersonBuilder& name(std::string v) { _name = v; return *this; }
    PersonBuilder& age(uint32_t v)    { _age = v;  return *this; }
    PersonBuilder& email(std::string v){ _email = v; return *this; }

    Person build() {
        if (!_name || !_age || !_email) throw std::runtime_error("missing fields");
        return {*_name, *_age, *_email};
    }
};
```

> **C++ analysis**: The builder pattern in C++ is purely manual — you write the builder class by hand, duplicating every field and setter. Template metaprogramming can't iterate over struct member names because C++ lacks compile-time reflection (until C++26's proposed static reflection). Libraries like `boost.hana` can help with `BOOST_HANA_DEFINE_STRUCT`, but they require annotating the struct definition. PenguinLang's `#fun derive_builder` is a single generic function that works on any class.

**TypeScript**

```typescript
// TypeScript can enforce this at the type level, but it requires heavy type gymnastics:
type Builder<T> = {
    [K in keyof T]: (value: T[K]) => Builder<Omit<T, K>>;
} & { build(): T };

// And even then, you'd need a complex implementation or a library like:
//   type-fest, zod, etc.
// A simpler runtime approach (losing compile-time safety):
function createBuilder<T extends object>(): any { /* ... */ }
```

> **TypeScript analysis**: TypeScript can theoretically express "all fields must be set" at the type level using mapped types and `Omit`, but this requires complex type gymnastics and often breaks down for larger objects. Practical implementations either use runtime validation (like `zod`) or code generation. PenguinLang's approach is simpler: the meta function generates straightforward Penguin code, and the compiler's existing type system handles the rest.

---

### Example 3: Interface → HTTP Client Stub Generation

**Problem**: Given an `interface` describing a service's methods, automatically generate an HTTP client that maps method calls to `fetch()` invocations — including URL construction, serialization, and error handling.

**PenguinLang**

> ⚠️ **`[v2]`** — uses the deferred rich reflection API: `compiler().get_methods`, `m.parameters`, `m.return_type`, and `params.to_signature()`.

```penguin
interface IUserService {
    fun getUser(id: u64) -> User;
    fun createUser(data: CreateUserRequest) -> User;
    fun deleteUser(id: u64) -> bool;
}

#fun derive_http_client(iface: type, base_url: string) -> ast {
    let methods = compiler().get_methods(iface);
    let client_name = iface.to_string() + "HttpClient";
    let mut method_impls = "";

    let i: u64 = 0;
    while (i < methods.size()) {
        let m = methods.at(i).some;
        let params = m.parameters;
        let return_type = m.return_type;
        let http_method = derive_http_method(m.name);       // "getUser" → "GET"
        let url_path = derive_url_path(m.name, params);     // "getUser" → "/users/{id}"

        method_impls = method_impls +
            "    fun " + m.name + "(mut this, " + params.to_signature() + ") -> " + return_type + " {\n" +
            "        let url: string = this.base_url + \"" + url_path + "\";\n" +
            "        let response: string = _http_" + http_method + "(url);\n" +
            "        return parse_" + return_type + "(response);\n" +
            "    }\n";

        i = i + 1;
    }

    return compiler().create_ast(
        "class " + client_name + " {\n" +
        "    base_url: string;\n" +
        method_impls +
        "}\n" +
        "impl " + iface.to_string() + " for " + client_name + " {}"
    );
}

#derive_http_client(#typeof(IUserService), "https://api.example.com");
```

**C++** (gRPC / protobuf code generation)

```protobuf
// users.proto
service UserService {
  rpc GetUser(GetUserRequest) returns (User);
  rpc CreateUser(CreateUserRequest) returns (User);
  rpc DeleteUser(DeleteUserRequest) returns (DeleteUserResponse);
}
```

```bash
protoc --grpc_out=. --cpp_out=. users.proto
```

> **C++ analysis**: gRPC's approach is to define the service in a separate IDL (`.proto` file) and run an external code generator (`protoc`). This requires a build-system step, the generated code is not human-readable or editable, and the IDL is a separate language to learn. PenguinLang's approach keeps everything in PenguinLang: the interface definition is the source of truth, and the meta function generates the client inline during compilation.

**TypeScript** (tRPC)

```typescript
import { initTRPC } from '@trpc/server';
import { z } from 'zod';

const t = initTRPC.create();

const userRouter = t.router({
    getUser: t.procedure.input(z.object({ id: z.number() })).query(({ input }) => {
        return { id: input.id, name: "Alice" };
    }),
    createUser: t.procedure.input(z.object({ name: z.string() })).mutation(({ input }) => {
        return { id: 1, name: input.name };
    }),
});
```

> **TypeScript analysis**: tRPC achieves type-safe API generation through a combination of runtime schema definition (`zod`) and TypeScript's type inference. While powerful, it requires adopting the tRPC runtime and schema library, and the API surface is coupled to the tRPC framework. PenguinLang's approach generates plain Penguin code with no external dependencies.

---

### Example 4: Compile-Time Structural Invariant

**Problem**: An `HttpError` enum has N variants, and an `error_messages` array must have exactly N elements (one message per variant). If a developer adds a variant but forgets to add a message, it should be a compile-time error.

**PenguinLang**

> ⚠️ **Partly `[v2]`.** The enum-variant count (`e.variants().size()`) is v1. The fixed-array-size check (`resolve_symbol(...).get_array_size()`) needs array-type reflection, which is **deferred** — v1 would pass the expected count as an explicit argument instead.

```penguin
enum HttpError {
    NotFound;          // 404
    Unauthorized;      // 401
    InternalError;     // 500
    BadRequest;         // 400
}

#fun assert_enum_coverage(e: type, array_name: string) {
    let variant_count = e.variants().size();
    // The array is sized at compile time; we can check it in a meta function
    let array_type = compiler().resolve_symbol(array_name).get_bound_type();
    let array_size = array_type.get_array_size();

    #if (variant_count != array_size) {
        compiler().error(
            "Enum " + e.to_string() + " has " + variant_count.to_string() +
            " variants but " + array_name + " has " + array_size.to_string() +
            " elements. They must match."
        );
    }
}

// error_messages must have exactly 4 elements:
let error_messages: [4]string = [
    "Resource not found",
    "Authentication required",
    "Internal server error",
    "Bad request",
];

#assert_enum_coverage(#typeof(HttpError), "error_messages");
```

**C++** (manual `static_assert`)

```cpp
enum class HttpError { NotFound, Unauthorized, InternalError, BadRequest };

constexpr const char* error_messages[] = {
    "Resource not found",
    "Authentication required",
    "Internal server error",
    "Bad request",
};

// Must manually update this count when adding variants:
static_assert(
    std::size(error_messages) == 4,
    "error_messages count must match HttpError variant count"
);
```

> **C++ analysis**: C++'s `static_assert` requires a **manually maintained** constant (the number `4` above). If you add a variant to `HttpError`, you must also update the `static_assert` constant — otherwise the assertion either fires incorrectly or becomes stale. With `magic_enum::enum_count<HttpError>()` you can automate this, but it's a third-party dependency. PenguinLang's `compiler().get_enum_variants()` is a first-class API.

**TypeScript**

```typescript
enum HttpError { NotFound, Unauthorized, InternalError, BadRequest }

const errorMessages = [
    "Resource not found",
    "Authentication required",
    "Internal server error",
    "Bad request",
] as const;

// TypeScript can enforce array length at the type level:
type AssertLength<T extends readonly any[], N extends number> =
    T['length'] extends N ? true : `Expected ${N} elements, got ${T['length']}`;

// But this only produces a type error, not a clear message,
// and cannot iterate over enum variants automatically.
type Check = AssertLength<typeof errorMessages, 4>; // must keep "4" in sync
```

> **TypeScript analysis**: Tuple type narrowing can enforce exact lengths, but the count must still be manually kept in sync with the enum definition. There's no way to query the number of enum variants at the type level without a helper library. The error message is often cryptic (`Type 'false' does not satisfy the constraint 'true'`).

---

### Example 5: Type-Level Function — Automatic Comparison Strategy Selection

**Problem**: Write a generic `safe_equals(a, b)` function that selects the appropriate comparison strategy based on the types of a and b: floating-point types use epsilon comparison, strings use locale-aware comparison, and all other types use `==`.

**PenguinLang**

```penguin
#fun ChooseComparison(t: type) -> type {
    // Introspect the type to pick the best comparison strategy
    #if (t == #typeof(f32) || t == #typeof(f64)) {
        // Floating point: use epsilon comparison
        return #typeof(FloatComparator);
    } #elif (t == #typeof(string)) {
        // Strings: use locale-aware comparison
        return #typeof(StringComparator);
    } #elif (compiler().can_compile_expression("let a: t; let b: t; a == b;")) {
        // Default: operator== is available
        return #typeof(DefaultComparator);
    } #else {
        compiler().error("Type " + t.to_string() + " is not comparable");
    }
}

class FloatComparator {
    impl IComparator {
        fun equals(a: f64, b: f64) -> bool {
            let diff = a - b;
            if (diff < 0.0) { diff = -diff; }
            return diff < 0.000001;
        }
    }
}

interface IComparator {
    fun equals(a: t, b: t) -> bool;  // (conceptual — would need to be generic)
}

#template(T: type)
fun safe_equals(a: T, b: T) -> bool {
    let comparator = #ChooseComparison(T).new();
    return comparator.equals(a, b);
}
```

**C++** (`if constexpr` + `concept` + type traits — three separate mechanisms)

```cpp
#include <type_traits>
#include <concepts>
#include <cmath>
#include <string>

// Strategy 1: floating-point epsilon
template<typename T>
requires std::floating_point<T>
bool safe_equals(T a, T b) {
    return std::fabs(a - b) < 1e-6;
}

// Strategy 2: string comparison
template<typename T>
requires std::same_as<T, std::string>
bool safe_equals(const T& a, const T& b) {
    return a == b; // could add locale logic here
}

// Strategy 3: default equality
template<typename T>
requires (!std::floating_point<T> && !std::same_as<T, std::string>)
    && std::equality_comparable<T>
bool safe_equals(const T& a, const T& b) {
    return a == b;
}

// Usage:
safe_equals(3.14, 3.1400001);  // true (epsilon)
safe_equals(std::string("hi"), std::string("hi")); // true
safe_equals(42, 42);           // true
```

> **C++ analysis**: The C++ solution uses three completely different language features: `if constexpr` for compile-time branching, `concept` / `requires` for type constraints, and `type_traits` for type introspection. The dispatch logic is spread across multiple overloads with `requires` clauses — solving the same problem that PenguinLang's single `#if` / `#elif` chain handles. C++20 concepts improve readability, but pre-C++20 this would be even more verbose with SFINAE.

**TypeScript**

```typescript
type Comparator<T> = (a: T, b: T) => boolean;

const floatComparator: Comparator<number> = (a, b) => Math.abs(a - b) < 1e-6;
const stringComparator: Comparator<string> = (a, b) => a === b;
const defaultComparator = <T>(a: T, b: T) => a === b;

// Dispatch via conditional types + overloads:
function safeEquals<T extends number>(a: T, b: T): boolean;
function safeEquals<T extends string>(a: T, b: T): boolean;
function safeEquals<T>(a: T, b: T): boolean;
function safeEquals<T>(a: T, b: T): boolean {
    if (typeof a === 'number') return floatComparator(a as number, b as number);
    if (typeof a === 'string') return stringComparator(a as string, b as string);
    return defaultComparator(a, b);
}
```

> **TypeScript analysis**: TypeScript's overload signatures provide compile-time type selection, but the implementation always uses runtime `typeof` checks — the dispatch logic is runtime, not compile-time. The type system cannot generate different function bodies for different types. PenguinLang's `#if` is truly compile-time: only the selected branch survives to the final binary, with zero runtime overhead.

---

### Example 6: Embedded DSL — State Machine Code Generation

**Problem**: Define a state machine by annotating methods with states/transitions, then automatically generate the `transition(event)` dispatch function and validate that all states are reachable and all transitions are handled.

**PenguinLang**

> ⚠️ **`[v2]`** — uses `Map` (not yet in stdlib), `compiler().get_methods_with_attribute`, and `m.get_attribute("state").value`, all deferred.

```penguin
// User writes: annotate methods with @state
class DoorStateMachine {
    // @state(Closed)
    fun on_open(mut this) -> string {
        return "Door is now open";
    }

    // @state(Open)
    fun on_close(mut this) -> string {
        return "Door is now closed";
    }

    // @state(Open)
    fun on_lock(mut this) -> string {
        return "Door is now locked";
    }
}

// Meta function: scans for @state annotations, builds the transition table
#fun derive_state_machine(cls: type) -> ast {
    let methods = compiler().get_methods_with_attribute(cls, "state");
    let mut transitions = "";
    let mut state_enum_variants = "";

    // Build enum: State { Closed; Open; Locked; }
    let mut states_seen = new Map<string, bool>();
    let i: u64 = 0;
    while (i < methods.size()) {
        let m = methods.at(i).some;
        let state = m.get_attribute("state").value;
        if (!states_seen.has_key(state)) {
            states_seen.set(state, true);
            state_enum_variants = state_enum_variants + "    " + state + ",\n";
        }
        i = i + 1;
    }

    // Build event enum and transition table
    // Generate transition() that matches (state, event) → action
    return compiler().create_ast(
        "enum State {\n" + state_enum_variants + "}\n" +
        "fun transition(mut this, event: string) -> string {\n" +
        "    // Auto-generated dispatch based on @state annotations\n" +
        generated_transition_body + "\n" +
        "}"
    );
}

#derive_state_machine(#typeof(DoorStateMachine));
```

**C++** (Boost.Statechart or Boost.SML)

```cpp
#include <boost/sml.hpp>
namespace sml = boost::sml;

struct Closed {};
struct Open {};
struct Locked {};

struct open_event {};
struct close_event {};
struct lock_event {};

auto door_sm = sml::state_machine<
    struct DoorSM {
        auto operator()() const {
            using namespace sml;
            return make_transition_table(
                *state<Closed> + event<open_event>  = state<Open>,
                 state<Open>   + event<close_event> = state<Closed>,
                 state<Open>   + event<lock_event>  = state<Locked>
            );
        }
    }
>{};
```

> **C++ analysis**: Boost.SML uses advanced template metaprogramming (hundreds of lines of TMP) to implement a compile-time state machine DSL. While powerful, it requires understanding of expression templates, the compilation errors are notoriously cryptic, and the library adds significant build time and binary size. PenguinLang's approach generates straightforward imperative code.

**TypeScript** (XState v5)

```typescript
import { createMachine } from 'xstate';

const doorMachine = createMachine({
    id: 'door',
    initial: 'closed',
    states: {
        closed: {
            on: { OPEN: 'open' },
        },
        open: {
            on: {
                CLOSE: 'closed',
                LOCK: 'locked',
            },
        },
        locked: {
            on: { UNLOCK: 'closed' },
        },
    },
});
```

> **TypeScript analysis**: XState provides a declarative API with excellent TypeScript support, but it's a runtime library (~20KB minified). The state machine is interpreted at runtime; there is no compile-time validation of transition completeness. PenguinLang's meta function approach validates the state machine at compile time and generates zero-overhead native code.

---

## Summary: PenguinLang vs C++ vs TypeScript

| Capability | PenguinLang | C++ | TypeScript |
|---|---|---|---|
| Compile-time computation | `#fun` + JIT (native speed) | `constexpr` / template metaprogramming | Limited (conditional types only) |
| Type reflection | `t.fields()` / `t.methods()` / `t.variants()` (reuse compiler's bound types) | None (until C++26 proposals) | `keyof`, `typeof` (type-level only) |
| AST-level code generation | `#fun -> ast` (structured) | External code generators / macros | Compiler plugins / transformers |
| Compile-time conditionals | `#if` (simple, readable) | `if constexpr` + preprocessor `#if` | Conditional types |
| Custom type constraints | `#fun` + `can_compile_expression` | C++20 `concept` + `requires` | Conditional types + `extends` |
| Loop unrolling | `#for` / `#while` (direct syntax) | `index_sequence` + fold expressions | N/A (no compile-time loops) |
| Variadic code generation | `ast` trailing block | Variadic templates / fold expressions | Union types + rest parameters |
| Learning curve | Single meta programming model | 4+ separate mechanisms | Type-level programming is TypeScript "wizard mode" |
