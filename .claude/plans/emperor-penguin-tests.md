# Plan: 迁移测试到 EmperorPenguin.Tests 并移除 PenguinLangCodeGenerator

## Context

将 PenguinLangCodeGenerator.Tests 中有价值的测试迁移到 EmperorPenguin.Tests，然后删除 PenguinLangCodeGenerator 项目及其测试项目。

## 关键设计决策

1. **不再使用 milestone 概念** — 所有测试针对 EmperorPenguin/src/ast/ 中的最新代码
2. **文件逐个传递** — 每个 .penguin 文件单独 AddFile，不合并为单字符串
3. **RoundTrip 测试** — 编译完整 EmperorPenguin 项目（含 main.penguin），通过 CLI 参数传递临时文件路径
4. **测试分类到不同文件** — ASTBuildTextTest.cs, ASTRoundTripParseTest.cs, ASTTokenizationTest.cs

## 文件结构

```
EmperorPenguin.Tests/
├── Common.cs                    — 共享 TestBase
├── ASTBuildTextTest.cs          — AST 节点 build_text 测试（~55 个）
├── ASTRoundTripParseTest.cs     — Parse round-trip 测试（~90 个）
└── ASTTokenizationTest.cs       — Tokenization 测试（~23 个）
```

## Helper 方法设计

### 共享的文件路径解析

```csharp
private static string AstDir => Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                  "EmperorPenguin", "src", "ast"));
```

### ASTBuildTextTest 的 helper
编译 EmperorPenguin 的 AST 代码（不含 main.penguin）+ 用户代码：
```csharp
private string RunWithEmperorPenguin(string userCode)
{
    var compiler = new SemanticCompiler(new ErrorReporter(new StringWriter()));
    compiler.AddFile(Path.Combine(AstDir, "Token.penguin"));
    compiler.AddFile(Path.Combine(AstDir, "Lexer.penguin"));
    compiler.AddFile(Path.Combine(AstDir, "AST.penguin"));
    compiler.AddFile(Path.Combine(AstDir, "Parser.penguin"));
    compiler.AddSource(userCode);
    var model = compiler.Compile();
    var vm = new BabyPenguinVM(model);
    vm.Run();
    return vm.CollectOutput().Trim();
}
```

### ASTRoundTripParseTest 的 helper
使用完整 EmperorPenguin 项目（含 main.penguin），将待测源码写入临时文件并通过 CLI 传递：
```csharp
private string ParseRoundTrip(string source)
{
    var tempFile = Path.Combine(Path.GetTempPath(), $"emperor_test_{Guid.NewGuid():N}.penguin");
    File.WriteAllText(tempFile, source);
    try
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "EmperorPenguin", "EmperorPenguin.penguins"));
        var compiler = new SemanticCompiler(new ErrorReporter(new StringWriter()));
        compiler.AddProject(projectPath);
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Global.CommandLineArgs = new[] { tempFile };
        vm.Run();
        return vm.CollectOutput().Trim();
    }
    finally { File.Delete(tempFile); }
}
```

对于 parse_expression/parse_statement 等（非 compilationUnit），需要不同的 main 入口：
```csharp
private string ParseWithMethod(string source, string parseMethod)
{
    var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"");
    var userCode = @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let p = new parser.Parser(tokens);
    let result = p.{parseMethod}();
    println(result.build_text());
}}";
    return RunWithEmperorPenguin(userCode);
}
```

### ASTTokenizationTest 的 helper
```csharp
private string Tokenize(string source)
{
    var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"")
                        .Replace("\n", "\\n").Replace("\r", "\\r");
    var userCode = @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let i: mut i64 = 0;
    while (i < cast<i64>(tokens.size())) {{
        let t = tokens.at(cast<u64>(i)).some;
        println(cast<string>(t.token_type) + "": "" + t.text);
        i = i + 1;
    }}
}}";
    return RunWithEmperorPenguin(userCode);
}
```

## 测试迁移

### 第一批：ASTBuildTextTest.cs（~55 个）
来源：IntegrationTests.cs 的 `AST_*_BuildText` 测试
- `RunWithMilestone3AST(...)` → `RunWithEmperorPenguin(...)`
- 测试代码体不变（AST 类名/字段名一致）

### 第二批：ASTRoundTripParseTest.cs（~90 个）
来源：`ParseRoundTrip_*`、`Milestone5/6/7_Parse*`、`Gap_Parse*` 测试

方法名映射：
- `ParseRoundTripMilestone3_5("1+2")` → `ParseWithMethod("1+2", "parse_expression")`
- `ParseWithMilestone5/6/7("...")` → 直接用 `ParseWithMethod(source, method)`
- `parse_letDeclaration()` → `parse_statement()` （EmperorPenguin 没有前者）
- `parse_breakStatement()` / `parse_continueStatement()` → `parse_jumpStatement()`
- `parse_compilationUnit` 相关测试 → `ParseRoundTrip(source)` 用 main.penguin CLI

### 第三批：ASTTokenizationTest.cs（~23 个）
来源：`Phase5_Tokenize*` 测试
- `TokenizeWithFullGrammar(...)` → `Tokenize(...)`
- 需验证 TokenType 名称与 EmperorPenguin/src/ast/Token.penguin 一致

## 删除旧项目

1. 编辑 `penguin-lang.sln` 移除 PenguinLangCodeGenerator 和 PenguinLangCodeGenerator.Tests
2. 删除 `PenguinLangCodeGenerator/` 和 `PenguinLangCodeGenerator.Tests/` 目录

## 验证

1. `dotnet build` 成功
2. `dotnet test EmperorPenguin.Tests` — 所有迁移测试通过
3. `dotnet test` — 全解决方案通过（BabyPenguin.Tests + EmperorPenguin.Tests）
