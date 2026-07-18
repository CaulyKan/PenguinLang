# EmperorPenguin Meta Programming — Implementation Plan

> Target: EmperorPenguin Pass3  
> Status: Design Phase  
> Last Updated: 2025-07-18  

## 1. Overview

This document describes the architecture, design rationale, and phased implementation plan for adding compile-time meta programming to the EmperorPenguin compiler. Meta programming features will **only** be implemented in EmperorPenguin Pass3; BabyPenguin remains unchanged.

### 1.1 Scope

| Feature | Included? | Notes |
|---|---|---|
| `#fun` meta function | ✅ | Pass3 only, JIT execution via LLVM ORC |
| `#if` / `#for` / `#while` | ✅ | Compiler hardcoded special-case; no JIT required |
| `#template` | ✅ | Already implemented (generics); treated as `#fun` sugar |
| `#typeof(T)` | ✅ | Two-level compile-time resolution |
| `#compiler()` | ✅ | Returns native EmperorPenguin compiler context object |
| `#define` / `#value` / `#defined` | ✅ | Syntax sugar over `compiler().set_option/get_option/has_option` |
| `ast` / `type` parameter types | ✅ | Aliases for AST node pointer / `BoundType` pointer |
| AST code generation (`#fun -> ast`) | ✅ | Meta function returns structured AST; compiler splices inline |
| Variadic AST (trailing `{ }`) | ✅ | Trailing code block always passed as `ast` typed parameter |

### 1.2 Relation to Bootstrap

```

  Phase 1 ──►  Phase 2    ──►     Phase 3          ──►  Phase 4
  pass1         pass2            pass3                  pass4
  (limited)     (limited         (FULL: meta             (final)
                 compiles          programming
                 pass3)           enabled)

```

Meta programming requires LLVM ORC JIT, which in turn requires linking `libLLVM`. This is only possible once Pass3 has a full-featured compiler capable of generating LLVM IR that references external C libraries — a capability that Pass2 (compiled by the limited Pass1) may not fully support. The initial implementation will therefore target Pass3 bootstrapped by a manually-augmented Pass2 build.

---

## 2. Design Decisions

### 2.1 Execution Engine: LLVM ORC JIT

**Choice: LLVM ORC JIT (in-memory compilation)**  
**Rejected: shared library dlopen, BabyPenguin VM IPC, tree-walking interpreter**

**Rationale:**
- Meta programming dynamically produces many small functions (especially for `#template` instantiations). Spawning a `.so` per function via `dlopen` would incur prohibitive I/O overhead.
- EmperorPenguin and the JIT'd code share the same runtime (GC heap, type system, memory allocator) — both are compiled by the same LLVM backend with identical data structure layouts. Passing `BoundType*`, `AST*` pointers between them is pointer-identical.
- An interpreter would be simpler but far slower for meta functions that do heavy computation (e.g., recursive type analysis, string processing for code generation).
- ORC JIT's lazy compilation model maps naturally to "compile meta function on first use, cache the function pointer thereafter".

### 2.2 Compiler Context Exposure

**Choice: Directly expose EmperorPenguin's compiler objects as `#compiler()` return value**  
**Future: Abstract behind an interface for API stability**

**Rationale:**
- EmperorPenguin is itself written in Penguin. The compiler's internal state — `SemanticModel`, `BoundCompilationUnit`, `BoundScope`, `BoundTypeRegistry` — are all Penguin objects on the GC heap.
- JIT'd code shares the same GC heap with the host compiler. Returning a reference to a compiler context object is a natural, zero-cost operation.
- An interface abstraction would add indirection without immediate benefit; it can be retrofitted once the API surface stabilizes.

### 2.3 #if / #for / #while: Hardcoded Special-Case

**Choice: Compiler directly evaluates `#if` conditions in-process (no JIT)**  
**Rejected: Implement `#if` as a built-in `#fun`**

**Rationale:**
- `#if`/`#for`/`#while` are conceptually meta functions (`#fun if(cond: bool, body: ast) -> ast`), but implementing them via JIT would introduce a bootstrap chicken-and-egg problem: the compiler needs `#if` to compile itself, but `#if` requires the JIT infrastructure which itself depends on the compiler.
- Hardcoding them is simpler and faster. The code path is essentially: evaluate a boolean condition in the host Penguin process, then either keep or discard the corresponding AST subtree.
- This does **not** preclude later refactoring into true built-in meta functions when the JIT infrastructure is stable.

### 2.4 Meta Function Execution: Lazy On-Demand Compilation

**Choice: Compile each meta function on first call; cache the function pointer**  
**Rejected: Batch topological-sort compilation**

**Rationale:**
- Meta function calls are discovered incrementally during the compiler pipeline (e.g., `pass_resolve_types` finds `#signed_to_unsigned(i32)`, `pass_bind_expressions` finds `#if (Debug)`). Pre-compiling all meta functions at startup would waste time on unused functions.
- ORC JIT natively supports lazy compilation. Each `#fun` is compiled to an LLVM module on first use and added to the JIT session.
- Recursive meta functions (e.g., `#fun fib`) are handled naturally: during JIT compilation of `fib`, the self-referential call to `fib` resolves to the module's own symbol.

### 2.5 AST Splicing: Structured AST

**Choice: `ast` type is a pointer to structured AST nodes (`Expression`, `Definition`, `Statement`). Meta functions that generate code return these structured nodes.**  
**Helper: `compiler().create_ast(code_string)` for text → AST conversion.**

**Rationale:**
- Structured AST enables subsequent compiler passes (type resolution, symbol binding) to continue seamlessly on the spliced code without re-lexing/re-parsing.
- Source location tracking is preserved (AST nodes carry `SourceLocation`).
- The `create_ast()` helper provides an escape hatch for text-based code generation, useful for simple fragments.

### 2.6 Meta Call Syntax

**Grammar:** `'#' identifier ('(' parameter_list ')')? (';' | code_block)`

**Rules:**
- The trailing `code_block` (if present) is always passed as the last `ast`-typed parameter to the meta function.
- If no trailing `code_block`, the call must end with `;`.
- A standalone `#name` (without `()`) followed by a definition block (e.g., `class A {}`) is equivalent to `#name() { class A {} }` — the entire trailing definition is passed as `ast`.

### 2.7 #typeof(T): Two-Level Compile-Time Semantics

**Choice: `#typeof(T)` resolves the type `T` at the JIT compilation time of the enclosing meta function, embedding a `BoundType*` constant into the generated native code.**

**Rationale:**
- In non-meta context: `#typeof(i32)` is resolved during the host compiler's pipeline; it produces a `BoundType*` constant.
- In meta context: `#typeof(i32)` inside a `#fun` body is resolved when the meta function is JIT-compiled; by that point, all type names in scope are known.
- `compiler().resolve_type(name: string) -> type` is the dynamic counterpart for cases where the type name is computed at meta-execution time.

### 2.8 can_compile Restriction

**Choice: `compiler().can_compile_expression(expr: string) -> bool` — expression-only, no side effects.**

**Rationale:**
- `can_compile` inherently requires re-entering the compiler pipeline from within JIT'd code. A full `can_compile` (allowing statements/definitions) could mutate global compiler state, introducing subtle re-entrancy bugs.
- Expression-only probing covers the 90% use case (checking if a type supports `+`, `<`, `.foo()`, etc.) while being side-effect-free.
- Can be relaxed to full `can_compile` once the compiler is proven re-entrant-safe.

### 2.9 GC Interaction: Conservative Stack Scan

**Choice: Trust the conservative mark-sweep GC (existing `gc.c`) to scan JIT stack frames.**

**Rationale:**
- Both the host compiler (Pass3 binary) and JIT'd code are produced by LLVM; their stack frame layouts are equally legible to conservative scanning.
- Meta functions are typically short-lived and rarely trigger GC. Even if a false positive retains a dead object, the memory will be reclaimed eventually.
- JIT'd code does not directly allocate GC objects; all heap manipulation goes through `compiler()` API, which runs in the host context.

### 2.10 LLVM Integration: C Wrapper Library

**Choice: `libpenguin_jit` — a small C library wrapping LLVM ORC JIT behind a pure C API, callable from Penguin via `extern`.**

**Rationale:**
- LLVM's native API is C++; Penguin's FFI only supports C `extern` functions.
- A thin C wrapper (`penguin_jit_init`, `penguin_jit_add_module`, `penguin_jit_lookup`, `penguin_jit_shutdown`) is sufficient for the meta programming use case.
- Only Core + ORCJIT + native target backend components need to be linked, keeping binary size manageable.

### 2.11 #define / #value / #defined: Compiler Option Sugar

**Choice: `#define("K", "V")` is syntactic sugar for `compiler().set_option("K", "V")`. `#value("K")` → `compiler().get_option("K")`. `#defined("K")` → `compiler().has_option("K")`.**

**Rationale:**
- These are essentially a compile-time key-value store. Realizing them through the `#compiler()` API unifies the concept without needing a separate mechanism.
- Similar to C preprocessor macros but type-safe and scoped to the compilation unit.

### 2.12 `type` and `ast` Runtime Representation

**Choice: `type` is `BoundType*`, `ast` is AST node pointer. Both are GC-tracked reference types.**  
**Future: Formalize as builtin `class MetaType` / `interface IAst` in stdlib.**

**Rationale:**
- EmperorPenguin's compiler objects are already Penguin objects. No new primitive types needed.
- JIT'd code and host code share pointer representations directly.
- Formalizing as classes/interfaces can be done later without breaking existing meta functions.

---

## 3. Architecture

### 3.1 System Diagram

```
                        ┌────────────────────────────────────┐
                        │        EmperorPenguin Pass3         │
                        │                                     │
  Source ──► Lexer ──► Parser ──► 9-Pass Pipeline            │
                        │   │         │                       │
                        │   │   AST nodes:                    │
                        │   │   MetaFunctionDefinition        │
                        │   │   MetaCallExpression            │
                        │   │   MetaIfBlock / MetaForBlock    │
                        │   │   MetaTypeofExpression          │
                        │   │   MetaCompilerExpression        │
                        │   │   MetaDefineStatement           │
                        │   │         │                       │
                        │   │   ┌────▼───────────────────┐    │
                        │   │   │  MetaExecutionEngine   │    │
                        │   │   │                        │    │
                        │   │   │  ┌──────────────────┐  │    │
                        │   │   │  │  #if/#for/#while  │  │    │
                        │   │   │  │  (direct eval)    │  │    │
                        │   │   │  └──────────────────┘  │    │
                        │   │   │                        │    │
                        │   │   │  ┌──────────────────┐  │    │
                        │   │   │  │  #fun JIT path    │  │    │
                        │   │   │  │  1. AST→Bound     │  │    │
                        │   │   │  │  2. Bound→IR      │  │    │
                        │   │   │  │  3. IR→LLVM IR    │  │    │
                        │   │   │  │  4. ORC compile    │  │    │
                        │   │   │  │  5. call fn ptr   │  │    │
                        │   │   │  └──────┬───────────┘  │    │
                        │   │   │         │               │    │
                        │   │   │  ┌──────▼───────────┐  │    │
                        │   │   │  │ libpenguin_jit    │  │    │
                        │   │   │  │ (C wrapper)       │  │    │
                        │   │   │  │                    │  │    │
                        │   │   │  │ LLVM ORC JIT      │  │    │
                        │   │   │  │  - addModule()    │  │    │
                        │   │   │  │  - lookup()       │  │    │
                        │   │   │  └───────────────────┘  │    │
                        │   │   └─────────────────────────┘    │
                        │   │         │                       │
                        │   │   spliced AST / values           │
                        │   │         │                       │
                        │   ▼         ▼                       │
                        │  LLVMEmitter ──► .ll ──► clang ──► exe │
                        └────────────────────────────────────┘
```

### 3.2 Data Flow for `#fun` Execution

```
1. Parser produces MetaFunctionDefinition(name="fib", params=[n: u32], return=u32, body=AST)

2. pass_build_scopes registers fib in global scope as BoundMetaFunctionDefinition

3. Some pass encounters #fib(10):
   a. MetaExecutionEngine.lookup("fib") → not yet compiled

   b. MetaExecutionEngine.compile("fib"):
      - Take fib's body AST
      - Walk through full EmperorPenguin pipeline:
        SemanticModel.pass_resolve_types → pass_bind_symbols → ... → pass_bind_expressions
        → IRGenerator.generate → LLVMEmitter.lower
      - Obtain LLVM IR text for the meta function body
      - penguin_jit_add_module(ctx, ir_text)
      - fn_ptr = penguin_jit_lookup(ctx, "fib")
      - Cache fn_ptr in BoundMetaFunctionDefinition.compiled_ptr

   c. Call fn_ptr(10) → returns 55 (native u32)

   d. Replace #fib(10) call site with constant 55

4. Subsequent #fib(20): lookup → cached fn_ptr found → call directly
```

### 3.3 #compiler() Context Passing

```
JIT'd meta function has access to compiler context via:

  LLVM IR: @compiler_context = external global %CompilerContext*

  Penguin: extern let compiler_context: mut CompilerContext;

  #compiler() → compiler_context

The host compiler sets @compiler_context before each meta function call,
pointing to the current SemanticModel / CompilerContext instance.
```

---

## 4. Syntax Specification

### 4.1 Meta Function Declaration: `#fun`

```
MetaFunctionDef  ::= '#fun' identifier '(' parameters? ')' ('->' type)? code_block
parameters       ::= parameter (',' parameter)*
parameter        ::= identifier (':' type)?    -- type defaults to 'ast'
type             ::= 'type' | 'ast' | primitive | identifier
```

**Constraints:**
- `#fun` only allowed at global scope or namespace scope
- Must have a return type annotation
- Parameters of type `ast` can only appear as the **last** parameter
- Recursive calls within the body do NOT need `#` prefix

**Examples:**
```penguin
#fun fib(n: u32) -> u32 { ... }
#fun signed_to_unsigned(t: type) -> type { ... }
#fun derive_clone(t: type) -> ast { ... }
#fun getter(field: ast) -> ast { ... }
#fun Addable(t: type) -> bool { ... }
```

### 4.2 Meta Function Call

```
MetaCallExpr      ::= '#' identifier ('(' args? ')')? (';' | code_block | definition)?
args              ::= expression (',' expression)*
```

**Rules:**
- If the meta function's last parameter type is `ast`, a trailing `code_block` or `definition` is bound to that parameter as a structured AST node
- If no trailing block, call must end with `;`
- In expression position: the return value replaces the call site
- In statement position: returned `ast` is spliced as statements
- In type position: returned `type` is spliced as type specifier

**Examples:**
```penguin
let x = #fib(10);                          // expression position
#getter() { x }                           // trailing code_block → ast param
#derive_clone(Point);                      // no trailing block
let t: #signed_to_unsigned(i32) = 0;      // type position
#print_all { "hello", 42, 3.14 };          // variadic ast, ignoring '()' for empty params
```

### 4.3 Compile-Time Conditional: `#if`

```
MetaIfStmt  ::= '#if' '(' expression ')' code_block
                ('#elif' '(' expression ')' code_block)*
                ('#else' code_block)?
```

**Rules:**
- Condition must be evaluable at compile time (constants, meta function calls, `#typeof`, `#defined`)
- All branches are compile-time; cannot mix runtime code
- Unevaluated branches are discarded and never parsed semantically

### 4.4 Compile-Time Loops: `#for`, `#while`

```
MetaForStmt    ::= '#for' '(' identifier 'in' expression '..' expression ')' code_block
MetaWhileStmt  ::= '#while' '(' expression ')' code_block
```

**Rules:**
- Loop bounds must be compile-time constants
- Body is duplicated (unrolled) for each iteration, with the loop variable replaced by its value
- `#while` may loop infinitely at compile time → compiler should have a configurable iteration limit

### 4.5 Type Query: `#typeof`

```
MetaTypeofExpr ::= '#typeof' '(' (identifier | qualified_identifier) ')'
```

**Semantics:**
- In non-meta context: resolves to `BoundType*` constant at host compile time
- In `#fun` body: resolves when the enclosing meta function is JIT-compiled
- The argument must be a type name visible in the current scope

### 4.6 Compiler Interface: `#compiler`

```
MetaCompilerExpr ::= '#compiler' '(' ')'
```

Returns the current `CompilerContext` object reference.

**CompilerContext API (initial):**

| Method | Signature | Description |
|---|---|---|
| `create_ast` | `(code: string) -> ast` | Parse text into AST node |
| `create_empty_ast` | `() -> ast` | Returns an empty AST (no-op) |
| `create_function_ast` | `(name: string, return_type: type, body: string) -> ast` | Generate a function AST |
| `can_compile_expression` | `(expr: string) -> bool` | Probe if expression compiles |
| `resolve_type` | `(name: string) -> type` | Resolve type by name |
| `resolve_symbol` | `(name: string) -> SymbolInfo` | Get symbol information |
| `get_fields` | `(t: type) -> List<FieldInfo>` | Get fields of a class |
| `error` | `(msg: string)` | Emit compile error |
| `warn` | `(msg: string)` | Emit compile warning |
| `set_option` | `(key: string, value: string)` | Set compile-time option |
| `get_option` | `(key: string) -> string` | Get compile-time option |
| `has_option` | `(key: string) -> bool` | Check if option is defined |

### 4.7 Compile-Time Symbols: `#define`, `#value`, `#defined`

```
MetaDefineStmt   ::= '#define' '(' string_literal ',' expression ')'
MetaValueExpr    ::= '#value' '(' string_literal ')'
MetaDefinedExpr  ::= '#defined' '(' string_literal ')'
```

**Semantics:** Syntactic sugar over `compiler().set_option/get_option/has_option`.

### 4.8 Generic Declaration: `#template`

```
TemplateDecl ::= '#' 'template' '(' template_param (',' template_param)* ')'
template_param ::= identifier (':' 'type')?
```

`#template(T: type) class Box<T> { ... }` is treated as syntax sugar. After parsing, the compiler conceptually transforms it into an equivalent `#fun` form. This transformation is internal to the compiler and not exposed to the user.

---

## 5. Execution Model

### 5.1 Layered Execution Timing

Meta calls are executed at different compiler passes depending on where they appear:

| Location | Executed In | Examples |
|---|---|---|
| Global / namespace scope | `pass_build_scopes` (early scan) | `#if (PLATFORM == "linux") { ... }` at top level |
| Type positions (return types, field types) | `pass_resolve_types` | `#signed_to_unsigned(T)` as return type |
| Inside class / enum / interface body | `pass_bind_symbols` | `#getter() { x }` inside class |
| Inside function / routine body | `pass_bind_expressions` | `#if (debug) { print(...); }` |
| Inside `#template` body | `pass_monomorphize` (at instantiation time) | `#if (T == i32) { ... }` |

### 5.2 Extended Monomorphization Fixpoint

```
loop:
  1. collect_generic_instantiations()      -- existing
  2. collect_meta_calls()                  -- NEW
  3. execute_meta_calls_in_pass()          -- NEW (JIT + call)
  4. splice_returned_ast_or_values()       -- NEW
  5. specialize_generics()                 -- existing
  6. resolve_types()                       -- existing
  7. if no_new_work: break                 -- existing check, extended
```

The loop terminates when no new generic instantiations are discovered AND no new meta calls need execution.

### 5.3 Caching

- Each `#fun` is compiled exactly once
- Compiled function pointer is cached in `BoundMetaFunctionDefinition.native_ptr`
- JIT'd modules are retained for the lifetime of the compilation session
- `compiler().can_compile_expression` results can be cached per type+expression pair

---

## 6. LLVM ORC JIT Integration

### 6.1 C Wrapper API (`libpenguin_jit`)

**Location:** `EmperorPenguin/std/c/penguin_jit.h` / `penguin_jit.cpp`

```c
// penguin_jit.h
#ifndef PENGUIN_JIT_H
#define PENGUIN_JIT_H

#ifdef __cplusplus
extern "C" {
#endif

typedef struct penguin_jit_ctx_s* penguin_jit_ctx_t;

// Create a new JIT compilation session.
// Returns NULL on failure.
penguin_jit_ctx_t penguin_jit_create(void);

// Add an LLVM IR module (as text) to the JIT session.
// Returns 0 on success, non-zero on failure.
int penguin_jit_add_module(penguin_jit_ctx_t ctx, const char* name,
                           const char* ir_text);

// Look up a symbol by name. Returns NULL if not found.
void* penguin_jit_lookup(penguin_jit_ctx_t ctx, const char* name);

// Destroy the JIT session and release all resources.
void penguin_jit_destroy(penguin_jit_ctx_t ctx);

// Get the last error message (thread-local).
const char* penguin_jit_get_error(void);

#ifdef __cplusplus
}
#endif

#endif // PENGUIN_JIT_H
```

**Dependencies:** LLVM Core, ORCJIT, native target (X86), native target info, execution engine.

### 6.2 Penguin Extern Declarations

In `core_builtin.penguin` (or a new `meta_builtin.penguin`):

```penguin
namespace __builtin {
    extern fun penguin_jit_create() -> u64;      // returns opaque handle
    extern fun penguin_jit_add_module(ctx: u64, name: string, ir_text: string) -> i32;
    extern fun penguin_jit_lookup(ctx: u64, name: string) -> u64;  // returns fn ptr
    extern fun penguin_jit_destroy(ctx: u64);
    extern fun penguin_jit_get_error() -> string;
}
```

### 6.3 Build Integration

The `EmperorPenguin/std/c/Makefile` is extended to optionally build `libpenguin_jit.a`:

```makefile
# LLVM config — may be overridden by environment
LLVM_CONFIG ?= llvm-config
LLVM_CXXFLAGS := $(shell $(LLVM_CONFIG) --cxxflags)
LLVM_LDFLAGS  := $(shell $(LLVM_CONFIG) --ldflags --libs core orcjit native)

libpenguin_jit.a: penguin_jit.cpp penguin_jit.h
	$(CXX) $(CXXFLAGS) $(LLVM_CXXFLAGS) -c penguin_jit.cpp -o penguin_jit.o
	$(AR) rcs libpenguin_jit.a penguin_jit.o
```

Pass3 compilation links `libpenguin_jit.a` + LLVM libraries:

```
clang combined.ll libcore_builtin.a libpenguin_jit.a $(LLVM_LDFLAGS) -o out.exe
```

---

## 7. Implementation Phases

### Phase 1: Token, AST & Parser Extension

**Files:** `ast/Token.penguin`, `ast/Lexer.penguin`, `ast/AST.penguin`, `ast/Parser.penguin`

- [ ] 1.1 Add `TokenType` variants:
  - `MetaFunKw` (for `#fun`)
  - `MetaIfKw` (for `#if`)
  - `MetaForKw` (for `#for`)
  - `MetaWhileKw` (for `#while` — or reuse existing `While`)
  - `MetaDefineKw` (for `#define`)
  - `MetaValueKw` (for `#value`)
  - `MetaDefinedKw` (for `#defined`)
  - `MetaTypeofKw` (for `#typeof`)
  - `MetaCompilerKw` (for `#compiler`)
  - `MetaElifKw` (for `#elif`)
- [ ] 1.2 Extend `Lexer` to recognize `#keyword` tokens (after `Hash` token, peek at next identifier)
- [ ] 1.3 Add AST node classes:
  - `MetaFunctionDefinition`: name, params (typed: `type`/`ast`/primitive), return_type, body
  - `MetaCallExpression`: func_name, args, trailing_block
  - `MetaIfBlock`: conditions[], bodies[], else_body
  - `MetaForBlock`: loop_var, range_start, range_end, body
  - `MetaWhileBlock`: condition, body
  - `MetaTypeofExpression`: type_name
  - `MetaCompilerExpression`: (unit)
  - `MetaDefineStatement`: key, value
  - `MetaValueExpression`: key
  - `MetaDefinedExpression`: key
- [ ] 1.4 Add `Expression` enum variant: `meta_call: MetaCallExpression`
- [ ] 1.5 Add `Statement` variant or handle meta constructs inline
- [ ] 1.6 Add `Definition` enum variant: `meta_fun_def: MetaFunctionDefinition`
- [ ] 1.7 Extend `Parser` to parse all `#`-prefixed constructs per the syntax spec

### Phase 2: Bound Layer Extension

**Files:** `bound/BoundDefinition.penguin`, `bound/BoundExpression.penguin`, `bound/BoundStatement.penguin`, `bound/BoundType.penguin`, `bound/BoundTypeRegistry.penguin`, `bound/BoundSymbol.penguin`

- [ ] 2.1 Add `TypeKind.MetaType`, `TypeKind.MetaAst` (or register as builtin class types)
- [ ] 2.2 Add `BoundMetaFunctionDefinition`:
  - name, full_name
  - params (typed: meta_type / meta_ast / primitive)
  - return_type (meta_type / meta_ast / primitive)
  - body: `BoundExpression`
  - compiled_native_ptr: `u64` (cache)
  - is_compiled: `bool`
- [ ] 2.3 Add `BoundDefinition.meta_fun_def: BoundMetaFunctionDefinition`
- [ ] 2.4 Add `BoundMetaCallExpression`, `BoundMetaTypeofExpression` to `BoundExpression`
- [ ] 2.5 Add `BoundMetaIfStatement`, `BoundMetaForStatement`, `BoundMetaWhileStatement` to `BoundStatement`
- [ ] 2.6 Add `BoundMetaDefineStatement`
- [ ] 2.7 Register `MetaType` and `MetaAst` as internal types in `BoundTypeRegistry`
- [ ] 2.8 Add `MetaCompilerSymbol` to `BoundSymbol` for `#compiler()` resolution

### Phase 3: C Runtime — libpenguin_jit

**Files:** `EmperorPenguin/std/c/penguin_jit.h`, `EmperorPenguin/std/c/penguin_jit.cpp`, `EmperorPenguin/std/c/Makefile`

- [ ] 3.1 Implement `penguin_jit_create()`, `penguin_jit_destroy()` using `llvm::orc::LLJIT`
- [ ] 3.2 Implement `penguin_jit_add_module()` using `LLJIT::addIRModule()` with `llvm::parseIR`
- [ ] 3.3 Implement `penguin_jit_lookup()` using `LLJIT::lookup()`
- [ ] 3.4 Implement error reporting via thread-local buffer
- [ ] 3.5 Update Makefile to compile `libpenguin_jit.a` when LLVM is available
- [ ] 3.6 Add extern declarations to `core_builtin.penguin`

### Phase 4: Meta Execution Engine

**New file:** `EmperorPenguin/src/meta/MetaEngine.penguin`

- [ ] 4.1 Create `MetaExecutionEngine` class:
  - `jit_ctx: u64` — JIT session handle
  - `compiled_funcs: Map<string, u64>` — name → native fn pointer cache
  - `compiler_context: CompilerContext` — reference for `#compiler()` calls
- [ ] 4.2 Implement `execute_meta_call(meta_call: BoundMetaCallExpression) -> MetaResult`
  - Lookup meta function definition in global scope
  - If not compiled: `compile_meta_function(meta_fun_def) → fn_ptr`
  - Marshal args (type → BoundType*, ast → AST*, primitive → value)
  - Call fn_ptr
  - Return result (type, ast, or value)
- [ ] 4.3 Implement `compile_meta_function(meta_fun_def: BoundMetaFunctionDefinition) -> u64`:
  - Take the meta function's bound body
  - Run through IRGenerator → LLVMEmitter to produce LLVM IR
  - Add compiler context global variable to the IR
  - Call `penguin_jit_add_module()` and `penguin_jit_lookup()`
  - Cache and return function pointer
- [ ] 4.4 Implement direct evaluators for `#if` / `#for` / `#while`:
  - `evaluate_meta_if(meta_if: BoundMetaIfStatement, scope) → AST`
  - `evaluate_meta_for(meta_for: BoundMetaForStatement, scope) → List<AST>`
  - `evaluate_meta_while(meta_while: BoundMetaWhileStatement, scope) → List<AST>`
- [ ] 4.5 Implement `#typeof` resolution: lookup type in scope, return BoundType*
- [ ] 4.6 Implement `#define` / `#value` / `#defined` as compiler option operations

### Phase 5: Pipeline Integration

**Files:** `bound/SemanticModel.penguin`, `bound/EmperorPenguinCompiler.penguin`

- [ ] 5.1 In `pass_build_scopes`:
  - Scan for global/namespace `#fun` definitions → register as `BoundMetaFunctionDefinition`
  - Evaluate global-scope `#if` to include/exclude top-level definitions
- [ ] 5.2 In `pass_resolve_types`:
  - When resolving a `TypeSpecifier` that is a `#f(...)` call, execute meta call → splice returned type
- [ ] 5.3 In `pass_bind_symbols` (for class/enum/interface bodies):
  - Scan for `#if`/`#for`/`#while`/`#f(...)` inside body
  - Execute → splice returned AST → continue symbol binding on spliced AST
- [ ] 5.4 In `pass_bind_expressions` (for function/routine bodies):
  - Scan for meta constructs in expression/statement positions
  - Execute → splice → continue binding
- [ ] 5.5 Extend `pass_monomorphize` fixpoint loop:
  - After collecting instantiations, collect meta calls from template bodies
  - Execute meta calls → splice → re-collect instantiations
  - Loop until fixpoint
- [ ] 5.6 Error handling: meta function execution errors carry source location from the original call site

### Phase 6: CompilerContext API

**New file:** `EmperorPenguin/src/meta/CompilerContext.penguin`

- [ ] 6.1 Define `CompilerContext` class:
  - References to `SemanticModel`, `BoundCompilationUnit`, `BoundTypeRegistry`
  - Methods: `create_ast`, `create_empty_ast`, `create_function_ast`
  - Methods: `can_compile_expression`, `resolve_type`, `resolve_symbol`
  - Methods: `get_fields`, `error`, `warn`
  - Methods: `set_option`, `get_option`, `has_option`
- [ ] 6.2 Implement `create_ast(code: string)`:
  - Lex + parse the code string
  - Return structured AST (parse in the context of the current scope)
- [ ] 6.3 Implement `can_compile_expression(expr: string)`:
  - Lex + parse the expression
  - Run through a minimal resolver (type check only, no side effects)
  - Return bool
- [ ] 6.4 Wire `CompilerContext` instance to the global `@compiler_context` before each meta function call

### Phase 7: Testing

**Files:** `Tests/MetaProgramming/*.md`

- [ ] 7.1 `CompileTimeFib.md`: `#fun fib` basic computation, verify constant folding
- [ ] 7.2 `TypeLevelMap.md`: `#fun` returning `type`, verify in type position
- [ ] 7.3 `CompileTimeIf.md`: `#if` / `#elif` / `#else` at global and function scope
- [ ] 7.4 `CompileTimeFor.md`: `#for` loop unrolling
- [ ] 7.5 `CodeGenerationGetter.md`: `#fun -> ast` generating getter methods
- [ ] 7.6 `CustomConcept.md`: `#fun Addable/Comparable` with `can_compile_expression`
- [ ] 7.7 `MetaRecursion.md`: Recursive `#fun` (fib, factorial)
- [ ] 7.8 `TypeofTest.md`: `#typeof` in meta and non-meta contexts
- [ ] 7.9 `DefineTest.md`: `#define` / `#value` / `#defined`
- [ ] 7.10 `MetaErrorTest.md`: Error messages with correct source locations from meta function bodies

---

## 8. Bootstrap Path

### 8.1 Dependency Chain

```
Pass1 (BabyPenguin VM) → compiles Pass2 (limited) → compiles Pass3 (full, +meta)
```

### 8.2 Challenges

1. **Pass2 must support `extern` C functions**: The `penguin_jit_*` functions are declared as `extern` in `core_builtin.penguin`. Pass2 must be able to emit LLVM IR that references external C symbols.

2. **Pass2 must link `libpenguin_jit.a`**: The Pass2-compiled Pass3 binary must link against the JIT library. This requires the Pass2 LLVM emitter to support emitting declarations for external link dependencies.

3. **LLVM library availability at build time**: `libpenguin_jit.a` depends on `libLLVM`. The EmperorPenguin std/c Makefile must handle the case where LLVM is not installed (skip building `libpenguin_jit.a`, Pass3 meta features unavailable).

### 8.3 Mitigation

- Pass2 may need to be manually augmented with the ability to emit `declare external` LLVM IR for the JIT functions
- A flag `--enable-meta` controls whether Pass3 initializes the JIT session
- Without JIT, Pass3 falls back to a useful subset: `#if`/`#for`/`#while` (hardcoded special-cases) + `#template` (existing generics) still work

---

## 9. Open Issues & Future Work

| Issue | Priority | Notes |
|---|---|---|
| `#compiler()` interface abstraction | Medium | After API surface stabilizes, wrap behind `interface ICompiler` |
| `can_compile` full statement/definition support | Low | After re-entrancy safety is proven |
| Meta function debugging support | Low | DAP integration for stepping into JIT'd meta functions |
| Meta function cross-compilation cache | Low | Serialize compiled meta function results for faster rebuilds |
| `#template` refactor to true `#fun` sugar | Low | Internally transform after JIT is stable |
| Iteration limit for `#while` | Medium | Pragmatic: default limit 10000, configurable via `#compiler().set_option` |
| Source location for spliced AST | Medium | Spliced AST nodes should carry a "generated by `#f` at `location`" annotation |
