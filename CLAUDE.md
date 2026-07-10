# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Penguin-lang is a concurrent-friendly programming language with C#-like syntax, inspired by C (syntax), C#/Java (garbage collection), Rust (type system), Go (coroutines), and Verilog/SystemC (concurrency).

**Main Components:**
- **BabyPenguin**: C# implementation of Penguin-lang compiler & VM, emits BabyPenguinIR (actively developed)
- **PenguinLangParser**: ANTLR4 grammar and parser for the language
- **MagellanicPenguin**: Language Server Protocol & Debug Adapter Protocol implementation
- **EmperorPenguin**: Penguin-lang compiler build from BabyPenguin, emits LLVM IR

## Compiler bootstrapping
There are different phases to bootstrapping a native full-powered emperor penguin compiler, see below:
1. EmperorPenguin pass 1: Running on BabyPenguin VM, limited features
2. EmperorPenguin pass 2: Recompile EmperorPenguin with EmperorPenguin pass 1, with limited features
3. EmperorPenguin pass 3: Recompile EmperorPenguin with EmperorPenguin pass 2, with full features 
4. EmperorPenguin pass 4: The final full featured EmperorPenguin

To run different EmperorPenguin compiler, use `emperor_penguin` script in root folder. e.g. `emperor_penguin -1 test.penguin`
To build self-bootstrapping emperorpenguin, use `emperor_penguin -b`

## Build and Development Commands

```bash
# Build the entire solution
dotnet build

# Run all tests
dotnet test

# Run specific test project
dotnet test BabyPenguin.Tests

# Run single test (example)
dotnet test --filter "FullyQualifiedName~BuiltinTest.PrintTest"

# Run a Penguin program
dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin

# Build self-contained executables
dotnet publish -r win-x64 --self-contained
dotnet publish -r linux-x64 --self-contained

# Build VSCode extension
cd MagellanicPenguin\vscode && npm run package
```

## Important Tips
* When writing penguinlang code, use skill penguin
* Always use max effort to implement function and test cases. Never use a easy but incorrect solution.
* When writing unit tests, you must use try to compare if full test output is correct. The use of ambiguous assertions is **prohibited**, such as comparing only queue sizes, string contains, etc.
* When debugging a codegen/runtime bug, write a minimal PenguinLang repro. Once the repro successfully reproduces the bug AND the fix is verified, **persist the repro as an e2e test** (e.g. add a `[BatchE2ETest]` case to the relevant `EndToEnd*Test.cs`) so the bug stays fixed. Do not discard working repros.

## Debugging with MCP DAP Tools

Claude Code has access to the `penguin-debug` MCP server for debugging PenguinLang programs via DAP (Debug Adapter Protocol). Use these tools to inspect program behavior step by step.

### Typical Debug Workflow

```
1. penguin_debug_launch    → Start debug session (with optional breakpoints)
2. penguin_debug_step_over → Step through code
3. penguin_debug_variables → Inspect variable values
4. penguin_debug_stack_trace → View call stack
5. penguin_debug_continue  → Continue to next breakpoint or completion
6. penguin_debug_output    → View compiler messages and debug logs
7. penguin_debug_disconnect → End session
```

### Available Tools

| Tool | Description |
|------|-------------|
| `penguin_debug_launch` | Compile and start debugging. Args: `program`, `stopOnEntry`, `breakpoints` |
| `penguin_debug_set_breakpoints` | Set breakpoints. Args: `file`, `breakpoints` (array of `{line, column?}`) |
| `penguin_debug_continue` | Continue execution until next stop or completion |
| `penguin_debug_step_over` | Step over current line |
| `penguin_debug_step_into` | Step into function call |
| `penguin_debug_step_out` | Step out of current function |
| `penguin_debug_stack_trace` | Get current call stack with source locations |
| `penguin_debug_variables` | Get local variables (optional `variablesReference` for nested objects) |
| `penguin_debug_evaluate` | Evaluate an expression |
| `penguin_debug_output` | Get diagnostic output (compiler messages, breakpoint status, debug logs) |
| `penguin_debug_status` | Query current debug session state |
| `penguin_debug_disconnect` | End debug session and get final output |

### Example: Debug with Breakpoints

```
penguin_debug_launch({
  program: "Examples/test.penguin",
  breakpoints: [{file: "Examples/test.penguin", lines: [{line: 2}, {line: 4}]}]
})
→ Stops at line 2, shows local variables

penguin_debug_step_over()
→ Advances one step, shows updated variables

penguin_debug_continue()
→ Runs to next breakpoint (line 4) or completion

penguin_debug_output()
→ Shows compiler diagnostics and debug logs
```

### Notes
- The `initial` block in user code runs inside `_ns_<name>.initial_0` function
- Breakpoints in builtin code may trigger before reaching user code; use `continue` to skip to user breakpoints
- The MCP server source is at `MagellanicPenguin/mcp-debug/` (TypeScript + `@modelcontextprotocol/sdk`)

## Error Handling

Use `BabyPenguinException` for errors with source location information. The `ErrorReporter` class handles diagnostic output with configurable verbosity levels (0-3).

## Type System

The language has explicit types with mutability modifiers. The type system is defined in `BabyPenguin/Type/` and supports:
- Primitive types (u8-u64, i8-i64, float, double, string, bool, char)
- Complex types (class, enum, interface, fun, arrays [])
- Generic types
- Type references (type alias)

See `Documentation/03_DataTypes.md` for detailed type information.

## BabyPenguin Architecture (C# Implementation)

BabyPenguin is the reference compiler & VM for PenguinLang, implemented in C#. It provides both compilation and execution capabilities.

### VM Architecture
- **Register-based VM**: Each function has a register array (not stack-based)
- **Yield/Async support**: Functions yield via `IEnumerable<RuntimeFrameResult>` for concurrent execution
- **Global object tracking**: All objects tracked in `RuntimeGlobal.AllObjects` with reference IDs
- **No JIT**: Purely interpretive execution

### IR Format
BabyPenguin emits a register-based IR with 25+ instruction types:
- **Value ops**: `CONST`, `ARG`, `ASSIGN`, `CAST`
- **Arithmetic**: `BINOP`, `UNARYOP`
- **Member access**: `RDMBR`, `WRMBR`
- **Control flow**: `BR`, `BR_COND`, `RET`, `RET_VOID`
- **Calls**: `CALL`, `CALL_VOID`
- **Objects**: `NEW`, `NEW_ENUM`
- **Enums**: `ISENUM`, `RDENUM`
- **Interfaces**: `ISINSTANCE`, `BOX`, `UNBOX`
- **Globals**: `GLOBAL_LOAD`, `GLOBAL_STORE`

### Runtime Value Types (C#)
- `BasicRuntimeValue`: Primitives (bool, i8-i64, u8-u64, f32, f64, char, string)
- `ReferenceRuntimeValue`: Objects with fields and reference tracking
- `FunctionRuntimeValue`: Function references with owner (fat pointers)
- `EnumRuntimeValue`: Enum variants with payloads

### Built-in Functions (provided by BabyPenguin VM)
- I/O: `print`, `println`, `eprint`, `eprintln`, `exit`
- String: `string_length`, `string_find`, `string_find_from`, `string_substring`, `string_char_at`, `string_char_code`, `string_to_int`
- File: `file_read_text`, `file_write_text`, `mkdir`, `file_exists`, `dir_exists`, `dir_get_entries`
- Process: `_exec_cmd`
- Misc: `lshift`, `rshift`, AtomicI64 operations

### Standard Library (PenguinLang)
- `Option<T>`: some/none enum with `is_some()`, `is_none()`, `value_or()`
- `Result<T,E>`: ok/error enum with `is_ok()`, `is_error()`, `value_or()`
- `List<T>`: Linked list with `push()`, `at()`, `set()`, `pop()`, `remove()`, `size()`
- `Queue<T>`: Linked queue with `enqueue()`, `dequeue()`, `peek()`, `size()`
- `StringBuilder`: `append()`, `to_string()`
- `Box<T>`: Simple wrapper class
- `ICopy<T>`: Interface for value-type copy semantics (implemented for all primitives)
- `IIterator<T>`, `IIterable<T>`: Iterator interfaces
- `Pair<K,V>`: Key-value pair class

## EmperorPenguin Architecture

EmperorPenguin is the self-hosting compiler (written in PenguinLang, compiled/run by BabyPenguin VM). It processes `.penguin` source files through a multi-pass pipeline and emits LLVM IR as its final output.

### Project Configuration

`EmperorPenguin/EmperorPenguin.penguins` defines source roots:
```
sources=["src/ast/*.penguin", "src/bound/*.penguin", "src/ir/*.penguin", "src/llvm/*.penguin", "src/project/*.penguin", "main.penguin"]
```

### Source Structure (~16,000 lines total)

```
EmperorPenguin/src/
  ast/           -- AST layer (Parser 1808 lines, Lexer 964 lines, AST 1210 lines, Token 170 lines, SourceLocation 19 lines)
  bound/         -- Semantic analysis layer (SemanticModel 4260 lines, BoundTreePrinter 490 lines, BoundDefinition 248 lines, ...)
  ir/            -- IR generation layer (IRGenerator 1278 lines, IRInstruction 600 lines, IRBuilder 203 lines, ...)
  llvm/          -- LLVM IR emission (LLVMEmitter 2553 lines)
  project/       -- Project file parsing, glob resolution (Project.penguin 396 lines)
main.penguin     -- Entry point (180 lines)
```

### Entry Point Flow (main.penguin)

1. Parse command-line args → check for `.penguins` project file or direct `.penguin` files
2. Load `EmperorPenguin/std/penguin/core_builtin.penguin` as standard library
3. Build per-file `SourceInput` list
4. `EmperorPenguinCompiler.compile_sources(inputs)` → bound compilation result
5. Check for semantic errors → report with source locations
6. `IRGenerator.generate(result)` → IR module
7. `LLVMEmitter.lower(module, result)` → LLVM IR text
8. Allocate a per-process unique temp dir via `_utils.get_temp_folder()` (so parallel invocations never collide) and write `.ll` there
9. Run `make -C EmperorPenguin/std/c OUTPUT_DIR=<temp_dir>` to build C runtime (`libcore_builtin.a`) into that temp dir
10. Run `clang <temp_dir>/combined.ll <temp_dir>/libcore_builtin.a -o <exe>` to produce the native executable (exe path defaults to `<temp_dir>/out.exe`, overridden by `-o`)

### Bound Tree (Semantic Layer)

The bound tree sits between AST and IR. Key files in `src/bound/`:

| File | Contents |
|------|----------|
| `BoundType.penguin` | `Mutability`, `PrimitiveType`, `TypeKind`, `BoundType` class with `display_name()`, `is_same_type()`, `is_value_type()`, `is_reference_type()`, `with_mutability()`, `with_generic_args()` |
| `BoundTypeRegistry.penguin` | Primitive type pre-building, `resolve_type()` lookup, `can_implicitly_cast()` rules, `can_widen_primitive()` for numeric widening |
| `BoundSymbol.penguin` | `BoundVariableSymbol`, `BoundFunctionSymbol`, `BoundTypeSymbol`, `BoundEnumMemberSymbol`, `BoundNamespaceSymbol`, `BoundFunctionParameter`, aggregated by `BoundSymbol` enum |
| `BoundScope.penguin` | `ScopeKind` (Global/Class/Enum/Interface/Function/Block/InitialRoutine/Impl), `BoundScope` — hierarchical lookup with `lookup_symbol()`, `lookup_type_in_scope()`, `resolve_qualified()`, namespace merging |
| `BoundExpression.penguin` | 12 expression classes + `BoundExpression` enum |
| `BoundStatement.penguin` | 10 statement classes + `BoundStatement` enum |
| `BoundDefinition.penguin` | `BoundVTable`, 11 definition classes (Function, Class, Enum, Interface, Namespace, InitialRoutine, TypeReference, ClassField, GlobalVariable, InterfaceImpl, InterfaceForImpl) + `BoundDefinition` enum |
| `BoundCompilationUnit.penguin` | `SemanticError`, `BoundCompilationUnit` with definitions, global_scope, type_registry, errors |
| `BoundTreePrinter.penguin` | Debug printer for bound tree visualization |
| `EmperorPenguinCompiler.penguin` | Top-level compiler entry: `compile_sources()` orchestrates the full pipeline |
| `SemanticModel.penguin` | Multi-pass binding orchestrator (4260 lines) |

### Compiler Pipeline (SemanticModel) — All 9 Passes Implemented

1. **Pass 1 — Build Scopes** (`pass_build_scopes`): AST → BoundDefinitions + BoundScope tree + symbol registration. Handles all definition types: functions, classes, enums, interfaces, namespaces, initial routines, implementations, type references, global variables
2. **Pass 2 — Resolve Types** (`pass_resolve_types`): Walks AST and bound trees in parallel; resolves `ast.TypeSpecifier` → `BoundType` for return types, parameters, fields. Handles generics, qualified names, function types, mutability
3. **Pass 3 — Monomorphize** (`pass_monomorphize`): Generic instantiation for classes, enums, functions. Name mangling (`Foo__i32`, `identity__string`). Iterative approach for transitive dependencies (up to 10 iterations). Fixes up `this` parameter types for specialized methods
4. **Pass 4 — Bind Symbols** (`pass_bind_symbols`): Binds parameter symbols and completes symbol information for functions and fields
5. **Pass 5 — Constructors** (`pass_constructors`): Creates default constructors if none exist; processes explicit constructors marked with `is_new`
6. **Pass 6 — Interface Implementation** (`pass_interface_implementation`): Builds vtables for interface implementations. Handles both class and enum interface implementations. Processes `impl for` syntax
7. **Pass 7 — Classify Value Types** (`pass_classify_value_types`): Determines which classes are value types (ICopy) vs reference types (IRef). Classifies based on implemented interfaces and field types
8. **Pass 8 — Bind Expressions** (`pass_bind_expressions`): Binds all expression and statement types — literals, identifiers, binary/unary ops, function calls, member access, if/while, code blocks, casts, new expressions. Implements type checking, boxing/unboxing for interface casts, generic method calls
9. **Pass 9 — Validate Control Flow** (`pass_validate_control_flow`): Ensures non-void functions return on all paths. Validates break/continue in loops. Type checks return values

### IR Layer (`src/ir/`)

| File | Contents |
|------|----------|
| `IRModule.penguin` | Container for functions and global variables |
| `IRFunction.penguin` | Functions with parameters, instructions, registers |
| `IRInstruction.penguin` | 23 instruction types (CONST, BINOP, UNARYOP, ASSIGN, CAST, RDMBR, WRMBR, BR, BR_COND, RET, RET_VOID, CALL, CALL_VOID, CALL_VIRT, NEW, NEW_ENUM, ISENUM, RDENUM, ISINSTANCE, BOX, UNBOX, GLOBAL_LOAD, GLOBAL_STORE) |
| `IRGenerator.penguin` | Converts bound trees to IR. Handles vtable calls, boxing/unboxing, enum pattern matching, symbol registers |
| `IRBuilder.penguin` | Helper for building IR instructions |
| `IRValue.penguin` | IR value representation |
| `IRPrinter.penguin` | Debug printer for IR |
| `IRSourceLocation.penguin` | Source location tracking in IR |

### LLVM Emission (`src/llvm/`)

`LLVMEmitter.penguin` (2553 lines) — multi-pass LLVM IR text emitter:
1. **Pass 1**: Collect string literals as global constants
2. **Pass 2**: Build class/enum layout tables for LLVM struct definitions
3. **Pass 3**: Emit all functions, determine needed runtime declarations
4. **Final**: Combine type definitions, globals, declarations, and functions

**Type mapping**:
- Value types (ICopy) → stack-allocated LLVM structs (no metadata header)
- Reference types (IRef) → heap-allocated with metadata pointer at offset 0
- Enums → `{ ptr metadata, i32 tag, payload? }` tagged unions
- Strings → always `ref<string>` (GC-managed)
- Primitives → i8/i16/i32/i64, u8/u16/u32/u64, float, double, bool (i8), char (i32)

### C Runtime (`EmperorPenguin/std/c/`)

| File | Contents |
|------|----------|
| `core_builtin.c` | All built-in function implementations: print, string operations, GC allocation, type conversions (int→string, bool→string), string concatenation, file I/O, StringBuilder |
| `gc.c` | Conservative mark-sweep garbage collector with stack scanning. Root registration, automatic collection thresholds. Platform-specific (x86_64, aarch64) |
| `penguinlang_interop.c` | Runtime support: `_emperor_vtable_lookup()` for virtual dispatch, `_emperor_isinstance()` for interface checks, `_emperor_check_class()` for class type checks |
| `Makefile` | Builds `libcore_builtin.a` from the above sources. Accepts `OUTPUT_DIR` variable |

### Standard Library (`EmperorPenguin/std/penguin/`)

| File | Contents |
|------|----------|
| `core_builtin.penguin` (129 lines) | `__builtin` namespace: extern function declarations (exit, print, string ops), `Option<T>`, `Result<T,E>`, `Box<T>`, `StringBuilder`, `ICopy<T>`, `ICopy` impls for all primitives, `IIterator<T>`, `IIterable<T>`, `Pair<K,V>` |
| `utils.penguin` (188 lines) | `_utils` namespace: `List<T>` (linked list), `Queue<T>` (linked queue), file I/O externs, `exec()` helper, `dir_get_entries()` |

### Project Handling (`src/project/`)

`Project.penguin` (396 lines) in `project` namespace:
- `PenguinProject.load(path)`: Parse `.penguins` INI-style project files
- `resolve_sources(project_dir)`: Expand glob patterns (`*`, `**`, `?`) to actual `.penguin` file paths
- Helper functions: `string_ends_with`, `string_starts_with`, `string_trim`, `split_string`, `parse_string_array`, `path_combine`, `get_parent_dir`, `glob_match`, `expand_glob`, `collect_penguin_files`

### Verification Commands

```bash
# Verify EmperorPenguin code compiles/runs through BabyPenguin VM
dotnet run BabyPenguin.tests | tee /temp/test.log

# Run EmperorPenguin tests
dotnet test EmperorPenguin.Testst | tee /temp/test.log

# Run all tests (both BabyPenguin and EmperorPenguin)
dotnet test --verbosity normal | tee /temp/test.log
```

When doing tests, always tee full log to a file, avoid running expensive tests multiple times

### Test Infrastructure (EmperorPenguin.Tests)

Uses `BatchCompiler` with custom attributes:
- `BatchTest` / `InitBatch<T>()` — AST/parser tests
- `BatchE2ETest` / `InitE2EBatch<T>()` — End-to-end compilation tests (39 basic tests passing)
- `BatchBoundTest` / `InitBoundBatch<T>()` — Bound tree tests
- `BatchIRTest` / `InitIRBatch<T>()` — IR generation tests
- `BatchLLVMTest` / `InitLLVMBatch<T>()` — LLVM IR tests

Test pattern:
```csharp
private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<ClassName>();
[BatchE2ETest(/* penguin code */, /* expected output */)]
[Fact]
public void TestName() => batch.Assert();
```

### PenguinLang Mutability Patterns for Bound Objects

When writing PenguinLang code that modifies nested objects:

- **Constructors use `fun new(mut this, ...)`**: Always require `mut this`
- **`let x: mut T = value`**: Creates immutable binding to mutable value — can call `mut this` methods and assign to `mut` fields
- **`let mut x = value`**: Creates mutable binding with inferred type — cannot have type annotation
- **Enum variant access returns immutable values**: Cannot chain `.symbol.some.bound_type = ...` through enum variants. Must extract to `let sym: mut BoundFunctionSymbol = ...` first
- **Functions returning `mut T`**: Required when result is assigned to `mut` fields. Change return type from `T` to `mut T`
- **`List<T>.push()` needs `mut` list**: Declare as `let params: mut List<T>` or `let mut params = new List<T>()`

### Namespace Convention for Bound Types

All bound types live in the `bound` namespace. In test code (outside the namespace), use full paths: `bound.BoundType`, `bound.BoundScope`, etc. Builtin types (`Option`, `List`, `StringBuilder`) don't need namespace prefix.

### Known Limitations (from README)

- **Concurrency/Coroutines**: Parser supports `event`, `emit`, `on`, `wait`, `async`, `folk` but LLVM emitter doesn't generate state machines for stackless coroutines yet
- **Metaprogramming**: Parser supports `#fun`, `const if`, `const for` but semantic model skips metaprogramming execution
- **Attributes/Indexers**: Not yet implemented at the Bound layer
