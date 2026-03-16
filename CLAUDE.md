# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Penguin-lang is a concurrent-friendly programming language with C#-like syntax, inspired by C (syntax), C#/Java (garbage collection), Rust (type system), Go (coroutines), and Verilog/SystemC (concurrency).

**Main Components:**
- **BabyPenguin**: C# implementation of Penguin-lang compiler & VM, emits BabyPenguinIR (actively developed)
- **PenguinLangParser**: ANTLR4 grammar and parser for the language
- **MagellanicPenguin**: Language Server Protocol & Debug Adapter Protocol implementation
- **EmperorPenguin**: LLVM IR emitter (not started)

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

## Error Handling

Use `BabyPenguinException` for errors with source location information. The `ErrorReporter` class handles diagnostic output with configurable verbosity levels (0-3).

## Type System

The language has explicit types with mutability modifiers. The type system is defined in `BabyPenguin/Type/` and supports:
- Primitive types (u8-u64, i8-i64, float, double, string, bool, char)
- Complex types (class, enum, interface, fun, arrays [])
- Generic types
- Type references (type alias)

See `Documentation/03_DataTypes.md` for detailed type information.
