# 20. BabyPenguin AST（ANTLR4）

BabyPenguin 前端是生成的 ANTLR4 解析器外披一层手写的语法层。本页记录从源码文本到可供语义层使用的语法节点流水线。

## 文法

**`PenguinLangParser/PenguinLang.g4`**——455 行组合文法（`grammar PenguinLang;`）。要点结构：

- **优先级阶梯**（3–88 行）：`primaryExpression` → `postfixExpression`（成员访问、调用、`new`、`async`、`wait expr [tick]`、`wait`）→ unary → multiplicative → additive → relational → equality（`== != is`）→ 位 and/xor/or → 逻辑 and/or。
- **语句**（约 209 行）：if/while/for/赋值/跳转（`continue` / `break expr?`）/ `return` / `yield` / `__signal` / try-catch / `connect(a, b)`（RTL 连线，仅 `construct` 块内合法）。
- **定义**：`namespaceDefinition`、`classDefinition`（含 `portDeclaration` `input`/`output`、`initialRoutine`、`constructBlock`、`#template(...)` 参数）、`enumDefinition`、`interfaceDefinition`、`interfaceImplementation`（`impl T;`）、`interfaceForImplementation`（`impl I for T;`）、带 `pure/!pure/extern/async/!async` 说明符的 `functionDefinition`、`lambdaFunctionExpression`、try-bind（`let a [: T] := b`）。
- `compilationUnit: namespaceDeclaration* EOF`（约 300 行）——每个顶层声明都是可选 `export` 前缀的 `namespaceDeclaration` 条目。

**`PenguinLangParser/PenguinLangParser.csproj`** 用 `Antlr4BuildTasks` 12.14.0 + `Antlr4.Runtime.Standard` 4.13.1 构建文法；生成的 `PenguinLangParser`/`PenguinLangLexer`/上下文类在构建期位于 `obj/`。`<WarningAsError>CS8509</WarningAsError>` 强制生成代码中 switch 的穷尽性。

## 解析入口与错误呈现

**`PenguinLangParser/Parser.cs`**（123 行）：

- `PenguinParser.PrepareParser`（94 行）：`AntlrInputStream` → `PenguinLangLexer` → `CommonTokenStream` → `PenguinLangParser`，并在词法与语法两侧装自定义 `ErrorListener<S>`（移除默认控制台监听器）。
- `ErrorListener<S>.SyntaxError`（38–46 行）置 `HasError`、构造 `SourceLocation`、记录出错的 `ParserRuleContext`、经 `ErrorReporter` 输出。
- `ParserData.ReportError()`（81–92 行）：存在任何词法/语法错误时抛 `PenguinLangException("Failed to parse input, messages: …", ruleName, code: ErrorCode.E_PARSE)`。
- `ParseToSexp`（115 行）解析、遍历并序列化树的 S 表达式转储（`SexpSerializer.cs`，255 行）。

**`PenguinLangParser/ErrorReporter.cs`**（95 行）：`PenguinLangException(message, context, code)`；`DiagnosticLevel { Error, Warning, Info, Debug }`；`DiagnosticMessage.ToString()` 格式化为 `error[Code]: msg (at file:row,col)`。

**`PenguinLangParser/ErrorCode.cs`**（78 行）：**BabyPenguin 与 EmperorPenguin 共享**的错误码枚举——E_PARSE、E_RESOLVE_*、E_DUPLICATE_*、E_TYPE_*、E_GENERIC_*、E_INTERFACE_IMPL、E_WIRING、E_RUNTIME_* 等。EmperorPenguin 在 `src/ErrorCode.penguin` 中重新声明相同错误码，两个编译器因此发出一致的错误标识。

## AST 构造（SyntaxWalker + 逐节点 Build）

没有生成的 visitor。从 ANTLR 解析树到 AST 的转换由逐节点的 `Build(SyntaxWalker, ParserRuleContext)` 覆写完成：

- **`SyntaxNodes/SyntaxNode.cs`**（186 行）——抽象基类：`SourceLocation`、`SourceText`（原始源码切片，49 行）、`ScopeId`；经 `[ChildrenNode]` 特性的反射驱动 `Children`（109–136 行）；`TraverseChildren`、`ReplaceChild`；静态工厂 `SyntaxNode.Build<T>(walker, context)`（84–89 行）。
- **`SyntaxCompiler.cs`**（121 行）：`SyntaxCompiler.Compile()`（13–19 行）= 创建 `SyntaxWalker`，然后对每个编译单元 `SyntaxNode.Build<NamespaceDefinition>(walker, compilationUnitContext)`。`SyntaxWalker`（31–80 行）维护作用域栈、线程安全的单调 `ScopeId` 计数器与静态 `ConcurrentDictionary<uint,uint> ScopeParentMap`（供后续可见性遍历）。`SyntaxScopeType`（82–93 行）枚举作用域种类。
- `SyntaxNodes/` 下约 50 个节点类（最大：`PrimaryExpression.cs` 223 行、`NamespaceDefinition.cs` 219、`FunctionDefinition.cs` 187、`Statement.cs` 166、`ClassDefinition.cs` 125）。例：`FunctionDefinition.Build`（FunctionDefinition.cs 6–94 行）压入 Function 作用域、把 `this`/`mut this` 参数脱糖为合成的 `Declaration`（29–42 行）、把说明符 token 解码为 `IsExtern`/`IsPure`/`IsAsync`（64–86 行）。

**源位置**——`PenguinLangParser/SourceLocation.cs`（108 行）：`(FileName, FileNameIdentifier, RowStart, RowEnd, ColStart, ColEnd)` 记录；`SourceLocation.From(filename, ctx)`（15 行）为单 token 上下文计算结束位置；`Contains`、`GetText`（源码切片提取）与比较运算符。

## BabyPenguin 在哪里接入

`BabyPenguin/SemanticModel.AddSource`（SemanticModel.cs 593–605 行）：`PenguinParser.Parse` → `SyntaxCompiler.Compile()` → 每个语法 `NamespaceDefinition` 被包成语义 `Namespace` 节点加入模型。此后 9 个语义遍（见 [BabyPenguin Semantic](./21_BabyPenguinSemantic.md)）消费语法树。

## 与 EmperorPenguin AST 的对比

EmperorPenguin 使用手写词法/语法分析器（`src/ast/Lexer.penguin`、`Parser.penguin`）产出自己的 AST（见 [EmperorPenguin AST](./25_EmperorPenguinAST.md)）。ANTLR 文法与手写解析器接受同一语言，差异已知：`using` 与嵌套命名空间成员访问只在 EmperorPenguin 解析；`export` 位置不同（BabyPenguin：`export` 在整条声明之前（含 `#template` 之前）；EmperorPenguin：两种位置都接受）；除 `#template` 外的元编程构造只存在于手写解析器。其余部分由跨编译器测试套件保持同步。
