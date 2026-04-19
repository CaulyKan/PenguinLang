# Plan: EmperorPenguin AST 完善与代码清理

## Context

EmperorPenguin 的 AST/Parser 已通过 PenguinLangCodeGenerator 生成并通过 200 个测试。现在需要：
- 删除不再需要的 CodeGenerator 项目
- 直接维护 EmperorPenguin/src/ast/ 代码
- 修复 7 个跳过的测试（`<`/`>` 歧义 + 分号问题）
- 完善 AST（参数列表、变量声明、泛型参数存储）
- 增加错误报告和测试覆盖

## 关键文件

| 文件 | 用途 |
|------|------|
| `EmperorPenguin/src/ast/AST.penguin` | AST 节点定义 |
| `EmperorPenguin/src/ast/Parser.penguin` | 递归下降解析器 |
| `EmperorPenguin/src/ast/Lexer.penguin` | 词法分析器 |
| `EmperorPenguin/src/ast/Token.penguin` | Token 类型定义 |
| `EmperorPenguin.Tests/ASTRoundTripParseTest.cs` | Round-trip 测试 |
| `EmperorPenguin.Tests/ASTBuildTextTest.cs` | build_text 测试 |
| `EmperorPenguin.Tests/ASTTokenizationTest.cs` | 分词测试 |

## 步骤

### 第 1 步：删除 PenguinLangCodeGenerator 项目

PenguinLangCodeGenerator 不在 penguin-lang.sln 中，所以只需删除目录。

- 删除 `PenguinLangCodeGenerator/` 目录
- 删除 `PenguinLangCodeGenerator.Tests/` 目录（只保留 TestGrammars/ 如果有参考价值可保留，否则也删除）
- 验证 `dotnet build` 和 `dotnet test` 全部通过

### 第 2 步：删除 Parser.penguin 中的 stub 函数

Parser.penguin 中有约 18 个返回占位符的 `parse_xxx` 函数。这些函数的**功能已被上层函数内联处理**（如 `parse_unaryExpression` 直接内联了 `parse_unaryOperator` 的逻辑），它们本身不再被调用。

删除以下函数（保留 `parse_xxx` 仅当它被其他函数调用）：

**可以安全删除的（无调用者）：**
- `parse_unaryOperator` — 被 `parse_unaryExpression` 内联处理
- `parse_multiplicativeOperator` — 被 `parse_multiplicativeExpression` 内联
- `parse_additiveOperator` — 被 `parse_additiveExpression` 内联
- `parse_relationalOperator` — 被 `parse_relationalExpression` 内联
- `parse_equalityOperator` — 被 `parse_equalityExpression` 内联
- `parse_letKeyword` — 被 `parse_codeBlockItem` / `parse_statement` 内联
- `parse_storageClassSpecifier` — 被内联
- `parse_typeMutabilitySpecifier` — 被 `parse_typeSpecifier` 内联
- `parse_iterableType` — 被 `parse_typeSpecifier` 内联
- `parse_variadicGenericArguments` — 被内联
- `parse_nestedParenthesesBlock` — 未使用
- `parse_voidLiteral` — 被 `parse_primaryExpression` 内联
- `parse_returnKeyword` — 被 `parse_returnStatement` 内联

**保留但需修复的：**
- `parse_assignmentOperator` — 被单独调用
- `parse_identifierWithDotsAndGenericArguments` — 可能被调用
- `parse_genericArguments` — 被 `parse_identifierWithGeneric` 调用
- `parse_typeSpecifierWithoutIterable` — 被 `parse_typeSpecifier` 调用

删除前需确认每个函数是否被其他函数调用（grep 函数名）。

### 第 3 步：修复 IdentifierExpression 泛型参数存储

**问题**：`parse_identifierWithGeneric` 解析了泛型参数但丢弃了它们（line 157-165）。

**修改 AST.penguin**：
```penguin
class IdentifierExpression {
    name: mut string = "";
    generic_args: mut List<TypeSpecifier> = new List<TypeSpecifier>();  // 新增
    ...
}
```

更新 `build_text()` 生成 `name<T1, T2>` 格式。

**修改 Parser.penguin**：
`parse_identifierWithGeneric` 改为：
```penguin
fun parse_identifierWithGeneric(this) -> ast.Expression {
    let name_token: Token = this.stream.advance();
    let id_expr: ast.IdentifierExpression = new ast.IdentifierExpression();
    id_expr.name = name_token.text;
    if (this.stream.peek_type() is TokenType.Less) {
        // 调用已有的 parse_genericTypeArgs 存入 generic_args
        this.parse_genericTypeArgs(id_expr.generic_args);
    }
    return new ast.Expression.identifier(id_expr);
}
```

### 第 4 步：修复 `<` / `>` 泛型 vs 比较歧义

**核心原则**：
- 在**类型上下文**中（`:` 后、`->` 后、`<` 后、`cast<` 后）：`<` 只解析为泛型
- 在**表达式上下文**中：`<` 只解析为比较运算符

**问题定位**：
`parse_identifierWithGeneric` (line 157) 在表达式上下文中被 `parse_primaryExpression` 调用，遇到 `<` 就尝试解析泛型参数。例如 `x < 10` 被解析为 `x<10>`，然后找不到 `>` 而出错。

**修复方案 A — 表达式上下文中 `<` 永远是比较运算符**：

1. `parse_primaryExpression` 中不再调用 `parse_identifierWithGeneric`，改为只解析标识符名（不含泛型）
2. `parse_typeSpecifier` 继续调用 `parse_identifierWithGeneric`（含泛型）

表达式中的泛型将来在后续的语义分析中处理。

**这将修复 3 个跳过的测试**：`ParseRoundTrip_BinaryLessThan`、`ParseRoundTrip_WhileWithBinaryCondition`、`ParseRoundTrip_ComplexBinaryAllLevels`

### 第 5 步：修复代码块中的分号问题

**问题**：`{ if (1) { 2 }; 3 }` — `ifStatement` 的 then 分支是 `{ 2 }`（一个 codeBlockExpression），后面的 `;` 应该被 codeBlockItem 消费，但当前代码中 `parse_codeBlockExpression` 在处理 statement 后不消费可选的 `;`。

**修复**：在 `parse_codeBlockExpression` 的循环中，解析完一个 statement 后，如果下一个 token 是 `;`，消费它（作为空语句分隔符）。

```penguin
// 在 parse_codeBlockExpression 的循环末尾添加：
// 可选的 trailing semicolons after block-like statements
while (this.stream.peek_type() is TokenType.Semicolon) {
    this.stream.advance();
}
```

**这将修复 4 个跳过的测试**：`ParseRoundTrip_NestedWhileIf`、`ParseRoundTrip_NestedIfInCodeBlock`、`ParseRoundTrip_IfElseNestedCodeBlocks`、`ParseRoundTrip_CodeBlockWithIfAndWhile`

### 第 6 步：修复 parameterList

**问题**：`parse_parameterList` (line 1772) 返回占位符 `"params"` 字符串，不创建 Parameter 对象。

**修改 Parser.penguin**：
重写 `parse_parameterList` 使用已有 `Parameter` AST 节点：
```penguin
fun parse_parameterList(this) -> List<ast.Parameter> {
    let params = new List<ast.Parameter>();
    if (!(this.stream.peek_type() is TokenType.RParen)) {
        // 处理 this 参数
        let first: Token = this.stream.peek();
        if (first.token_type is TokenType.ThisKw || first.token_type is TokenType.Mut) {
            params.push(this.parse_thisParameter());
        } else {
            params.push(this.parse_singleParameter());
        }
        while (this.stream.match(new TokenType.Comma())) {
            if (this.stream.peek_type() is TokenType.RParen) { break; }
            params.push(this.parse_singleParameter());
        }
    }
    // 消费可选的 trailing comma
    if (this.stream.peek_type() is TokenType.Comma) {
        this.stream.advance();
    }
    return params;
}
```

同时需要实现 `parse_thisParameter` 和 `parse_singleParameter`，创建正确的 `Parameter` 节点。

更新 `FunctionDefinition` 和 `LambdaFunctionExpression` 的 `parameters` 字段类型为 `List<Parameter>`（如果当前不是的话）。

### 第 7 步：修复 declaration / VariableDeclaration

**问题**：`parse_declaration` 创建 `FunctionDefinition` 而非专门的声明节点。

**检查现有 AST**：`LetDeclarationStatement` 已存在（lines 491-507），但用于语句上下文。对于参数声明（`identifier ':' typeSpecifier`），需要使用 `Parameter` 节点。

**修改**：
- `parse_declaration` 应该根据上下文返回正确的节点
- 在参数上下文中，`parse_singleParameter` 创建 `Parameter` 节点
- 在 `codeBlockItem` 中的 `let` 声明已正确使用 `LetDeclarationStatement`

### 第 8 步：增加错误报告机制

**当前问题**：
- `TokenStream.expect()` 只 print 错误但继续执行
- 没有 error count 或 error list
- 没有行号/列号的系统化报告

**改进方案**：在 Parser 中添加 error collection：
```penguin
class Parser {
    stream: mut TokenStream;
    errors: mut List<string> = new List<string>();
    
    fun report_error(this, message: string) {
        let tok: Token = this.stream.peek();
        let err: string = "Line " + cast<string>(tok.line) + ", Col " + cast<string>(tok.col) + ": " + message;
        this.errors.push(err);
        print(err);
    }
    
    fun expect(this, expected: TokenType) -> Token {
        let token: Token = this.stream.advance();
        if (cast<string>(token.token_type) != cast<string>(expected)) {
            this.report_error("Expected " + cast<string>(expected) + " but got " + token.to_string());
        }
        return token;
    }
    
    fun has_errors(this) -> bool {
        return cast<i64>(this.errors.size()) > 0;
    }
}
```

在 `compilationUnit` 解析完成后检查 `has_errors()`，如果有错误则打印所有错误。

### 第 9 步：增加测试用例

为每个修复添加测试：

**泛型/比较歧义测试**（取消 Skip 并验证）：
- `x < 10` → round-trip 正确（比较运算符）
- `while (x < 10)` → round-trip 正确
- `a || b && c | d ^ e & f == g != h < i > j + k - l * m / n` → round-trip
- `List<i64>` → 类型上下文中泛型正确
- `cast<i64>(x)` → cast 泛型正确
- `new List<i64>()` → new 泛型正确

**分号测试**（取消 Skip 并验证）：
- `{ if (1) { 2 }; 3 }` → round-trip
- `{ if (1) { 2 }; while (x) { 3 }; 4 }` → round-trip

**参数列表测试**：
- `fun foo(x: i64) { }` → 参数列表正确 build_text
- `fun foo(x: i64, y: string) { }` → 多参数
- `fun foo(mut this) { }` → this 参数
- `fun foo(x: i64 = 42) { }` → 默认值参数

**IdentifierExpression 泛型测试**：
- `parse_typeSpecifier("List<i64>")` → generic_args 包含 `i64`
- `parse_typeSpecifier("Map<i64, string>")` → 多泛型参数
- `parse_expression("f(1)")` → 表达式中 `<` 不触发泛型

**错误报告测试**：
- `fun { }` → 报错缺少函数名
- `let x = ;` → 报错缺少表达式
- `{ if }` → 报错缺少条件

## 执行顺序

1. **第 1 步** — 删除 PenguinLangCodeGenerator（最简单，先清理）
2. **第 2 步** — 删除 stub 函数（清理代码）
3. **第 4 步** — 修复 `<` / `>` 歧义 
4. **第 5 步** — 修复分号问题
5. **第 3 步** — 修复 IdentifierExpression 泛型存储
6. **第 6 步** — 修复 parameterList
7. **第 7 步** — 修复 declaration
8. **第 8 步** — 增加错误报告
9. **第 9 步** — 增加测试

每步完成后运行测试验证。

## 验证

```bash
dotnet build
dotnet test EmperorPenguin.Tests   # 207 通过 + 0 跳过 + 0 失败
dotnet test BabyPenguin.Tests       # 235 通过 + 0 失败
```
