# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Penguin-lang is a concurrent-friendly programming language with C#-like syntax, inspired by C (syntax), C#/Java (garbage collection), Rust (type system), Go (coroutines), and Verilog/SystemC (concurrency).

**Main Components:**
- **BabyPenguin**: C# implementation of Penguin-lang compiler & VM, emits BabyPenguinIR (actively developed)
- **PenguinLangParser**: ANTLR4 grammar and parser for the language
- **MagellanicPenguin**: Language Server Protocol & Debug Adapter Protocol implementation
- **EmperorPenguin**: Penguin-lang compiler build from BabyPenguin, emits LLVM IR

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

## EmperorPenguin Architecture

EmperorPenguin is the self-hosting compiler (written in PenguinLang, compiled/run by BabyPenguin VM). It processes `.penguin` source files through a multi-pass pipeline:

### Project Configuration

`EmperorPenguin/EmperorPenguin.penguins` defines source roots:
```
sources=["src/ast/*.penguin", "src/bound/*.penguin", "src/ir/*.penguin", "main.penguin"]
```

### Source Structure

```
EmperorPenguin/src/
  ast/           -- AST nodes (AST.penguin, Parser.penguin, Lexer.penguin, Token.penguin)
  bound/         -- Semantic analysis layer
  ir/            -- IR generation 
main.penguin     -- Entry point
```

### Bound Tree (Semantic Layer)

The bound tree sits between AST and IR. Key files in `src/bound/`:

| File | Contents |
|------|----------|
| `BoundType.penguin` | `Mutability`, `PrimitiveType`, `TypeKind`, `BoundType` class |
| `BoundTypeRegistry.penguin` | Primitive type pre-building, type lookup, implicit cast rules |
| `BoundSymbol.penguin` | `BoundVariableSymbol`, `BoundFunctionSymbol`, `BoundTypeSymbol`, `BoundEnumMemberSymbol`, `BoundNamespaceSymbol`, aggregated by `BoundSymbol` enum |
| `BoundScope.penguin` | `ScopeKind`, `BoundScope` — hierarchical symbol lookup with namespace merging |
| `BoundExpression.penguin` | 12 expression classes + `BoundExpression` enum |
| `BoundStatement.penguin` | 10 statement classes + `BoundStatement` enum |
| `BoundDefinition.penguin` | `BoundVTable`, 10 definition classes + `BoundDefinition` enum |
| `BoundCompilationUnit.penguin` | `SemanticError`, `BoundCompilationUnit` |
| `SemanticModel.penguin` | Multi-pass binding orchestrator |

### Compiler Pipeline (SemanticModel)

1. **Pass 1 — Build Scopes** (`pass_build_scopes`): AST → BoundDefinitions + BoundScope tree + symbol registration
2. **Pass 2 — Resolve Types** (`pass_resolve_types`): Walks AST and bound trees in parallel; resolves `ast.TypeSpecifier` → `BoundType` for return types, parameters, fields
3. Future passes: Bind Symbols, Constructors, Interface Impl, Bind Expressions, Validate Control Flow

### Verification Commands

```bash
# Verify EmperorPenguin code compiles/runs through BabyPenguin VM
dotnet run --project BabyPenguin -- EmperorPenguin/EmperorPenguin.penguins

# Run EmperorPenguin tests
dotnet test EmperorPenguin.Tests

# Run all tests (both BabyPenguin and EmperorPenguin)
dotnet test
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
