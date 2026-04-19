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

## Architecture Overview

### Compiler Pipeline (BabyPenguin)

The compiler follows a multi-pass semantic analysis pattern:

1. **Parsing** (PenguinLangParser): ANTLR4 grammar generates syntax tree
2. **Semantic Analysis** (BabyPenguin/SemanticPass): Numbered passes (01-09) transform syntax tree:
   - 01_SemanticScoping: Scope and symbol table construction
   - 02_TypeElaborate: Type inference and checking
   - 03_SymbolElaborate: Symbol resolution
   - 04_Constructor: Constructor generation
   - 05_InterfaceImplementation: Interface implementation checking
   - 06_SyntaxRewriting: AST transformations
   - 07_CodeGeneration: BabyPenguinIR generation
   - 08_MainFunctionGeneration: Main routine setup
   - 09_CheckReturnValue: Return value validation

3. **Execution** (BabyPenguin/VirtualMachine): BabyPenguinVM executes generated IR

### Key Directories

- **BabyPenguin/SemanticNode**: Core AST nodes (ClassNode, Function, Namespace, etc.)
- **BabyPenguin/SemanticInterface**: Core interfaces for containers (ISymbolContainer, ITypeContainer, etc.)
- **BabyPenguin/Type**: Type system implementation
- **BabyPenguin/Symbol**: Symbol table implementation
- **BabyPenguin/VirtualMachine**: VM implementation (BabyPenguinVM, RuntimeFrame, RuntimeValue, etc.)
- **PenguinLangParser/SyntaxNodes**: Syntax tree nodes from ANTLR
- **PenguinLangParser/PenguinLang.g4**: Grammar file (modify and rebuild to update parser)

### Testing Pattern

Tests use xUnit with a `TestBase` class that provides output capture:

```csharp
public class MyTest(ITestOutputHelper helper) : TestBase(helper)
{
    [Fact]
    public void TestName()
    {
        var compiler = new SemanticCompiler(new ErrorReporter(this));
        compiler.AddSource(@"...");  // Penguin code
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Equal("expected output" + EOL, vm.CollectOutput());
    }
}
```

Test files in `BabyPenguin.Tests/` test specific language features. Test fixtures in `BabyPenguin.Tests/TestFiles/` are for integration tests.

## Language Syntax Notes

**Mutability modifiers**: `mut`, `!mut`, or auto-inferred
**Function types**: `fun<Type1, Type2>` for function pointers
**Async**: `async_fun` keyword, `async` expression for spawning, `wait` for yielding
**Generic syntax**: `ClassName<Type1, Type2>`
**Interfaces**: `impl IInterface for Type` for trait implementation
**Built-in types**: Option<T>, Result<T,E>, List<T>, Queue<T>, AtomicI64, StringBuilder, etc.

**Example syntax:**
```penguin
initial {
    let mut x: i64 = 0;
    let result: Option<i64> = some(42);
}
```

## Working with the Parser

When modifying `PenguinLangParser/PenguinLang.g4`:
1. The grammar uses ANTLR4
2. After changes, rebuild the solution - Antlr4BuildTasks will regenerate parser code
3. Generated code is in the `.antlr` directory and compiled automatically

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
