# 20. BabyPenguin AST (ANTLR4)

BabyPenguin's front end is a generated ANTLR4 parser wrapped by a hand-written syntax layer. This page documents the pipeline from source text to semantic-ready syntax nodes.

## Grammar

**`PenguinLangParser/PenguinLang.g4`** — a 455-line combined grammar (`grammar PenguinLang;`). Notable structure:

- **Precedence ladder** (lines 3–88): `primaryExpression` → `postfixExpression` (member access, calls, `new`, `async`, `wait expr [tick]`, `wait`) → unary → multiplicative → additive → relational → equality (`== != is`) → bitwise and/xor/or → logical and/or.
- **Statements** (~line 209): if/while/for/assignment/jump (`continue` / `break expr?`) / `return` / `yield` / `__signal` / try-catch / `connect(a, b)` (RTL wiring, legal only in `construct` blocks).
- **Definitions**: `namespaceDefinition`, `classDefinition` (with `portDeclaration` `input`/`output`, `initialRoutine`, `constructBlock`, `#template(...)` parameters), `enumDefinition`, `interfaceDefinition`, `interfaceImplementation` (`impl T;`), `interfaceForImplementation` (`impl I for T;`), `functionDefinition` with specifiers `pure/!pure/extern/async/!async`, `lambdaFunctionExpression`, try-bind (`let a [: T] := b`).
- `compilationUnit: namespaceDeclaration* EOF` (~line 300) — every top-level declaration is an optional-`export`-prefixed `namespaceDeclaration` entry.

**`PenguinLangParser/PenguinLangParser.csproj`** builds the grammar with `Antlr4BuildTasks` 12.14.0 + `Antlr4.Runtime.Standard` 4.13.1; the generated `PenguinLangParser`/`PenguinLangLexer`/context classes live in `obj/` at build time. `<WarningAsError>CS8509</WarningAsError>` forces switch exhaustiveness in the generated visitor switches.

## Parser Entry and Error Surfacing

**`PenguinLangParser/Parser.cs`** (123 lines):

- `PenguinParser.PrepareParser` (line 94): `AntlrInputStream` → `PenguinLangLexer` → `CommonTokenStream` → `PenguinLangParser`, with a custom `ErrorListener<S>` installed on both lexer and parser (default console listeners removed).
- `ErrorListener<S>.SyntaxError` (lines 38–46) sets `HasError`, builds a `SourceLocation`, records the offending `ParserRuleContext`, writes through `ErrorReporter`.
- `ParserData.ReportError()` (lines 81–92) throws `PenguinLangException("Failed to parse input, messages: …", ruleName, code: ErrorCode.E_PARSE)` when any lexer/parser error exists.
- `ParseToSexp` (line 115) parses, walks, and serializes an S-expression dump of the tree (`SexpSerializer.cs`, 255 lines).

**`PenguinLangParser/ErrorReporter.cs`** (95 lines): `PenguinLangException(message, context, code)`; `DiagnosticLevel { Error, Warning, Info, Debug }`; `DiagnosticMessage.ToString()` formats `error[Code]: msg (at file:row,col)`.

**`PenguinLangParser/ErrorCode.cs`** (78 lines): the error-code enum **shared by BabyPenguin and EmperorPenguin** — E_PARSE, E_RESOLVE_*, E_DUPLICATE_*, E_TYPE_*, E_GENERIC_*, E_INTERFACE_IMPL, E_WIRING, E_RUNTIME_*, etc. EmperorPenguin re-declares the same codes in `src/ErrorCode.penguin` so the two compilers emit identical error identifiers.

## AST Construction (SyntaxWalker + per-node Build)

There is no generated visitor. Conversion from the ANTLR parse tree to the AST happens through per-node `Build(SyntaxWalker, ParserRuleContext)` overrides:

- **`SyntaxNodes/SyntaxNode.cs`** (186 lines) — the abstract base: `SourceLocation`, `SourceText` (raw source slice, line 49), `ScopeId`; reflection-driven `Children` via the `[ChildrenNode]` attribute (lines 109–136); `TraverseChildren`, `ReplaceChild`; the static factory `SyntaxNode.Build<T>(walker, context)` (lines 84–89).
- **`SyntaxCompiler.cs`** (121 lines): `SyntaxCompiler.Compile()` (lines 13–19) = create a `SyntaxWalker`, then `SyntaxNode.Build<NamespaceDefinition>(walker, compilationUnitContext)` per compilation unit. `SyntaxWalker` (lines 31–80) maintains a scope stack, a thread-safe monotonic `ScopeId` counter, and a static `ConcurrentDictionary<uint,uint> ScopeParentMap` for later visibility walks. `SyntaxScopeType` (lines 82–93) enumerates the scope kinds.
- ~50 node classes under `SyntaxNodes/` (largest: `PrimaryExpression.cs` 223 lines, `NamespaceDefinition.cs` 219, `FunctionDefinition.cs` 187, `Statement.cs` 166, `ClassDefinition.cs` 125). Example: `FunctionDefinition.Build` (FunctionDefinition.cs lines 6–94) pushes a Function scope, desugars `this`/`mut this` parameters into a synthetic `Declaration` (lines 29–42), and decodes specifier tokens into `IsExtern`/`IsPure`/`IsAsync` (lines 64–86).

**Source locations** — `PenguinLangParser/SourceLocation.cs` (108 lines): a record of `(FileName, FileNameIdentifier, RowStart, RowEnd, ColStart, ColEnd)`; `SourceLocation.From(filename, ctx)` (line 15) computes end positions for single-token contexts; `Contains`, `GetText` (source-slice extraction), and comparison operators.

## Where BabyPenguin Hooks In

`BabyPenguin/SemanticModel.AddSource` (SemanticModel.cs lines 593–605): `PenguinParser.Parse` → `SyntaxCompiler.Compile()` → each syntax `NamespaceDefinition` is wrapped in a semantic `Namespace` node and added to the model. From there the 9 semantic passes (see [BabyPenguin Semantic](./21_BabyPenguinSemantic.md)) consume the syntax tree.

## Contrast with EmperorPenguin's AST

EmperorPenguin uses a hand-written lexer/parser (`src/ast/Lexer.penguin`, `Parser.penguin`) producing its own AST (see [EmperorPenguin AST](./25_EmperorPenguinAST.md)). The ANTLR grammar and the hand-written parser accept the same language except for known gaps: `using` and nested-namespace member access resolve only in EmperorPenguin; `export` placement differs (BabyPenguin: `export` before the whole declaration including `#template`; EmperorPenguin: both positions); metaprogramming constructs other than `#template` exist only in the hand-written parser. The two front ends are otherwise kept in lockstep by the cross-compiler test suite.
