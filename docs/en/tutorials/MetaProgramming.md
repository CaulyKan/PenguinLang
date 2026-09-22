# Metaprogramming

PenguinLang's metaprogramming lets ordinary PenguinLang code run **at compile time** and splice its results back into the program being compiled. The core ideas:

1. **The same language at compile time and runtime.** A compile-time function (`#fun`) is written with ordinary `fun` syntax — no separate macro language.
2. **Sequential code, not pattern matching.** Instead of C++ template specialization rules, compile-time decisions are `if`/`while` in a normal function.
3. **The compiler's own objects are the reflection API.** A `type` argument inside a `#fun` is a real `emperor.BoundType`; `t.fields()` returns the live field objects.

Metaprogramming is implemented by the self-hosted EmperorPenguin compiler. The `#fun` JIT paths run in the **native compilers only** (Pass2, Pass3, and the LSP's embedded compiler); the hardcoded constructs (`#define`, `#if`, `#while`) work in every EmperorPenguin build. BabyPenguin (the C# compiler) parses only `#template` and executes none of this. Full rules: the [Meta Programming specification](../specifications/11_MetaProgramming.md).

## Compile-Time Options and #if

`#define("K","V")` records a key/value pair in the compiler's option store; `#defined("K")` tests it, `#option("K")` reads it. The command line `-DMode=debug` writes into the same store. `#if` / `#elif` / `#else` select between definitions at compile time — each branch is a braced block of definitions, the condition folds from literals, `#defined`, `#option` comparisons, and `!`/`&&`/`||`:

```penguin
#define("Mode", "debug");

#if (#option("Mode") == "debug") {
    fun log_enabled() -> bool { return true; }
} #else {
    fun log_enabled() -> bool { return false; }
}
```

`#while` unrolls a compile-time loop (a 10000-iteration cap applies). `#for`, `#break`, `#continue` are parsed; their collection-iteration form is not implemented yet.

## #fun — Compile-Time Functions

`#fun` declares a function that the compiler JIT-executes (via LLVM ORC) when called. A call `#name(args)` is spliced into the program at its compile-time value — zero runtime cost:

```penguin
#fun sq(n: i64) -> i64 { return n * n; }

initial {
    let a: i64 = #sq(5);            // 25, computed during compilation
    let b: i64 = #sq(6) + #sq(2);   // 40
    println(cast<string>(a) + " " + cast<string>(b));
}
```

Rules in brief: `#fun` lives at global/namespace scope; recursion inside the body drops the `#`; parameters and return type are explicit; reference-typed returns are not allowed (compile-time flows carry `i64`, `bool`, `double`, `string`, `type`, and AST tokens).

### Type-Level Meta Functions

A `#fun` returning `type` computes a type. Use it anywhere a type is expected — the compiler runs it and splices the resulting type in:

```penguin
#fun num_kind(t: type) -> type {
    if (t.is_class()) { return #typeof(string); }
    return #typeof(i64);
}

initial {
    let n: #num_kind(i32) = 7;      // resolves to i64
    println(cast<string>(n));
}
```

`#template` is sugar for exactly this: `#template(T: type) class Box<T>` desugars to a `#fun Box(T: type) -> type` that returns the specialized class. Value parameters (`#template(N: i32)`) ride the same mechanism — the template's body is a meta function evaluated per instantiation.

## Reflection

Inside a `#fun`, a `type` parameter is the compiler's live type object. It carries real methods — there is no parallel `FieldInfo` hierarchy:

```penguin
class Point { x: i32; y: i32; fun norm(this) -> i32 { return 0; } }

#fun describe(t: type) -> i64 {
    if (t.is_class()) {
        return cast<i64>(t.fields().size());      // field count
    }
    return 0;
}

initial {
    println("fields=" + cast<string>(#describe(#typeof(Point))));   // fields=2
}
```

The commonly used members: `t.fields()` / `t.methods()` / `t.variants()` (live definition objects with `.name`, `.bound_type`, ...), `t.is_class()` / `is_enum()` / `is_interface()` / `is_primitive()`, `t.is_value_type()` / `is_reference_type()`, `t.display_name()`. `#typeof(T)` works in meta and non-meta code and resolves to the concrete instantiation inside a template body.

## #specializing — Conditional Implementations

A `#specializing <Type><Args>` block runs once per instantiation of a generic type and can inject interface implementations conditionally. The block body is ordinary compile-time code; an `impl` inside it attaches to the specialized type:

```penguin
#template(N: i32)
class foo {
    impl IReferenceType;
}

#specializing foo<N> {
    if (N > 3) {
        impl IDescribe {
            fun describe(this) -> string { return "big"; }
        }
    } else if (N == 2) {
        impl IDescribe {
            fun describe(this) -> string { return "two"; }
        }
    } else {
    }
}

initial {
    let a = new foo<5>();
    let da: IDescribe = a;
    println("n5=" + da.describe());    // n5=big
}
```

`foo<5>` implements `IDescribe`; `foo<1>` does not. This covers Rust where-clause / C++ partial-specialization territory with a plain `if`.

## #class and #compiler()

* **`#class`** declares a meta-only data class — bookkeeping for compile-time code that never reaches the generated program.
* **`#compiler()`** returns a proxy to the compiling compiler. In a `#fun` body: `compiler().error("...")` / `.warn(...)` / `.info(...)` emit diagnostics, `.set_option` / `.get_option` manage options, `.resolve_type("std.Vector")` resolves a type by name, and `.create_expression(text)` / `.create_definition(text)` parse source text into AST objects at meta-runtime.

## Code Generation: unstructured_ast

A `#fun` whose last parameter has kind `ast` (or `unstructured_ast` for raw text) accepts a **trailing code block** as syntax. The block is delivered to the meta function, which can inspect it, re-emit it, and return generated definitions to splice at the call site. The annotation form annotates a field:

```penguin
#fun tagged(tag: string, field: string, trailing_ast: unstructured_ast) -> ast {
    let probe: i64 = compiler().create_definition(trailing_ast);
    if (compiler().get_definition_kind(probe) != "class_field") {
        compiler().error("#tagged must annotate a field declaration");
    }
    return compiler().create_definition(
        "fun tag_of_" + field + "() -> string { return \"" + tag + "/" + field + "\"; } "
        + trailing_ast);
}

class C {
    #tagged("alpha")
    name: string = "field-ok";
}

initial {
    let c: mut C = new C();
    println(c.tag_of_name() + " " + c.name);   // alpha/name field-ok
}
```

The standard library's JSON serialization (`#impl_json_serializable()` in `json.penguin`) is written this way: reflect over fields, assemble impl source text, inject, compile normally. A larger worked example is `Examples/simple_sql`: SQL `SELECT`/`UPDATE`/`INSERT`/`DELETE` embedded in the language — a `#fun` meta call reads the SQL string literal at compile time, tokenizes it, parses the `WHERE` clause, reflects over the table class's fields, and splices a typed predicate back into the call site.

## What Runs Where

| Feature | BabyPenguin | EP Pass1 | EP Pass2/Pass3 / LSP |
|---|---|---|---|
| `#template` (type params) | parses & monomorphizes | parses & monomorphizes | full |
| `#define` / `#defined` / `#option` | — | ✓ | ✓ |
| `#if` / `#while` | — | ✓ | ✓ |
| `#fun` JIT, `-> type`, reflection, value templates, `#specializing`, `#class`, `#compiler()`, `-> ast` | — | — | ✓ |

Run the meta test suite with:

```bash
dotnet run --project Tests/PenguinTestRunner -- --filter "MetaProgramming/*" --compilers pass3
```

See the [specification](../specifications/11_MetaProgramming.md) for grammar, binding rules, execution passes, and the current not-implemented list.
