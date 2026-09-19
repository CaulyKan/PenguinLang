# Meta Programming

PenguinLang's metaprogramming runs ordinary PenguinLang code at compile time and splices the results back into the compiled program. The `#` prefix opens meta space: `#fun`, `#if`, `#while`, `#define`, `#typeof`, `#template`, `#specializing`, `#class`, `#compiler()`, and meta calls (`#name(args)`).

## Surface and Availability

| Construct | Kind | Availability |
|---|---|---|
| `#template(...)` prefix | sugar for a type-returning meta function | all compilers (parsed and monomorphized everywhere) |
| `#define("K","V")` / `#defined("K")` / `#option("K")` | compile-time key/value store | every EmperorPenguin build |
| `#if` / `#elif` / `#else`, `#while` | compile-time control flow (hardcoded folding) | every EmperorPenguin build |
| `#fun` (JIT meta functions), `-> type`, reflection, value template params, `#specializing`, `#class`, `#compiler()`, `-> ast` / `unstructured_ast` | meta engine (LLVM ORC JIT) | native compilers: Pass2, Pass3, LSP |

BabyPenguin (the C# compiler) parses only `#template` and executes none of the meta constructs. Pass1 (EmperorPenguin on the BabyPenguin VM) runs the hardcoded constructs but has no JIT.

## Execution Model

When a `#fun` call must be evaluated, the compiler compiles the `#fun` body (plus the compiler's own bound/AST type layer) through its own pipeline into LLVM IR and executes it on an embedded **LLVM ORC JIT**. Arguments and results cross the boundary as `i64` tokens; `type` arguments are interned tokens resolved back to live `BoundType` pointers. Reflection is **object reuse**: unit B (the compile-time unit) compiles the compiler's real `emperor.BoundType`/`BoundClassFieldDefinition`/`BoundFunctionDefinition` classes, so `t.fields()` inside a `#fun` is a direct method call on the live object.

Where a meta call is evaluated depends on its syntactic position:

| Location | Evaluated in |
|---|---|
| Global / namespace scope (definition position) | the pre-pass rewrite, before Pass 1 |
| Type position (`let x: #fun() -> type`) | Pass 2 (resolve types) |
| Inside class/enum/interface bodies | Pass 4 (bind symbols) |
| Inside function/routine bodies | Pass 8 (bind expressions) |
| Inside a `#template` body | Pass 3 (monomorphize, at instantiation) |

## Meta Functions: #fun

```penguin
#fun sq(n: i64) -> i64 { return n * n; }

initial {
    let a: i64 = #sq(5);        // spliced as the literal 25
}
```

Rules:

* `#fun` definitions live at global or namespace scope only.
* Parameters have explicit kinds — `type`, `ast`, `unstructured_ast`, or an ordinary value type (`i64`, `bool`, `double`, `string`, reference types map to `object`).
* The return type is explicit; allowed return kinds are the scalar types, `string`, `type`, and `ast`. Reference-typed returns into runtime code are not supported (compile-time flows only).
* Recursion inside the body drops the `#` prefix (plain calls).
* Compilation is on demand and cached per function.
* A call with too few arguments is a hard error; extra trailing `{ ... }` blocks are delivered only to an `unstructured_ast` last parameter.

A meta function returning `type` is usable in any type-specifier position: `let x: #signed_to_unsigned(i32) = 0;`. `#template` is sugar for this: `#template(T: type) class Box` behaves as `#fun Box(T: type) -> type`. Template value parameters (`#template(N: i32)`) are meta-evaluated per instantiation (native compilers only).

## Meta Call Syntax

```
'#' identifier ('(' args ')')? (';' | trailing_block)
```

* In expression position: `#name(args)` — the result is spliced at the call site (a `type` result in expression position is an error; use it in type position).
* In definition position: `#name(args)` followed by a braced block or a definition — the block/definition text is passed to the meta function (the annotation form; see `unstructured_ast` below).
* Unknown `#name` calls that are not registered `#fun`s fall back to a `BoundMetaCallExpression` that survives to later passes (the meta engine may still resolve them, e.g. deferred template re-binding).

## Compile-Time Options: #define / #defined / #option

`#define("K", "V")` writes to the compiler's option store; `#defined("K") -> bool` tests presence; `#option("K") -> string` reads. The CLI `-DK=V` writes into the same store. Inside `#fun` bodies these are also available as built-in meta functions and via `compiler().set_option/get_option/has_option`.

## Compile-Time Conditions: #if / #while

```penguin
#if (#defined("A")) { fun pick() -> string { return "a"; } }
#elif (#defined("B")) { fun pick() -> string { return "b"; } }
#else { fun pick() -> string { return "none"; } }
```

* Branch bodies are braced blocks of definitions (definition position) or statements (statement position).
* Conditions fold from literals, `#defined`, `#option` comparisons, `!`, `&&`, `||`, `==`, `!=`, and parentheses. Non-constant conditions are `E_UNSUPPORTED`.
* `#elif`/`#else` carry the `#` prefix (they are parser keywords, distinct from runtime `else`).
* `#while` unrolls with a 10000-iteration cap. `#for` / `#break` / `#continue` are parsed; collection iteration is not implemented.

## #typeof(T)

`#typeof(T)` yields the type token for `T`. It works inside `#fun` bodies (`#typeof(T) == #typeof(i32)` comparisons are pointer-equality on interned tokens) and in ordinary code, where it resolves to the concrete instantiated type inside a template body.

## Reflection API

Reuse-based — these names alias the compiler's own objects:

| Alias | Real object |
|---|---|
| `type` (a `#fun` `type` parameter) | `emperor.BoundType` |
| Field (`t.fields()` element) | `BoundClassFieldDefinition` |
| Method (`t.methods()` element) | `BoundFunctionDefinition` |
| Variant (`t.variants()` element) | `BoundEnumMemberDefinition` |

Type methods: `display_name()`, `kind_str()`, `is_class()`, `is_enum()`, `is_interface()`, `is_primitive()`, `is_value_type()`, `is_reference_type()`, `fields()`, `methods()`, `variants()`, `generic_args()`. Field/Method/Variant objects expose their data as plain fields (`name`, `bound_type`, ...). There is no annotation system.

A type token crosses into a `#fun` as an `emperor.BoundType` reference; dynamic resolution for computed names goes through `compiler().resolve_type`.

## #class

`#class` declares a meta-only data class: full class capabilities, compiled only into unit B, never emitted into the runtime program. Meta classes may use the reflection types directly and cannot be returned into runtime code.

## #compiler()

`#compiler()` returns a `CompilerContext` proxying the compiling compiler:

| Method | Effect |
|---|---|
| `error(msg)` / `warn(msg)` / `info(msg)` | emit compile-time diagnostics (`E_META`) |
| `set_option(k, v)` / `get_option(k) -> string` / `has_option(k) -> bool` | option store |
| `resolve_type(name) -> type` / `resolve_symbol(name)` / `has_type(name) -> bool` | resolution |
| `create_expression(text) -> ast` / `create_definition(text) -> ast` | parse source text at meta-runtime |
| `create_ast(...)` / `create_empty_ast()` / `create_function_ast(...)` | AST construction |
| `parse_arguments(text) -> ast` | parse an argument list (for variadic trailing blocks) |
| `get_ast(token) -> ast` | materialize a stored AST token |
| `get_current_scope() -> string` | the enclosing class during a class-member rewrite |
| `can_compile_expression(t)` | deferred (not implemented) |

`#compiler()` outside a `#fun` is `E_UNSUPPORTED`.

## AST Parameters and Code Generation

A `#fun` whose last parameter is `ast` (parsed) or `unstructured_ast` (raw text) accepts a trailing code block:

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
```

* An `ast`-returning meta call in expression position re-binds the returned expression at the call site.
* A definition-position call returning definitions splices them in (they flow through the remaining passes like hand-written code; the calling file's location is stamped on them).
* Variadic macros: a single `ast` parameter receiving `parse_arguments(text)` covers arbitrary argument lists.

## #specializing

A `#specializing <Type><Args>` block runs once per instantiation of a generic type, at monomorphization time. Its body is compile-time code; `impl` fragments inside it attach to the specialized type:

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
```

`#error("...")` inside a specializing block aborts compilation for that instantiation. Gates may be `#fun` calls (JIT-evaluated); activated impl slots are read back and injected by the monomorphizer.

## Extra Meta Sources

A `#fun` can call user code if the file is listed as a meta source: `--meta-src file.penguin` (or `meta-sources=[...]` in a project file). Meta sources are compiled into unit B verbatim and are the only user code visible at compile time (explicit list, no re-entry).

## Not Implemented

* `#for` over collections (parsed; splice expansion lands incrementally).
* Type-value method chains (`T::method()`-style); `can_compile_expression` (probing type capabilities) and custom-constraint libraries built on it.
* An annotation system (fields/methods carry no attribute metadata for reflection).
* `Map` in compile-time code.
