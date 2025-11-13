# 泛型语法重构：从 `<T>` 到 `#template`

本文档旨在规划和记录将 PenguinLang 的泛型声明语法从 `class A<T> {}` 形式重构为 `#template(T: type) class A {}` 形式的完整实施方案。

## 1. 目标

核心目标是替换现有的泛型声明语法，采用新的 `#template(...)` 指令。这一改动旨在：
-   使泛型/模板的意图更加明确。
-   为未来更复杂的元编程能力（例如，支持非类型模板参数 `N: i32`）提供语法基础。

## 2. 限制

-   在本次重构中，仅修改现有测试用例以适应新语法，**不添加**新的测试用例。
-   严格遵循本方案中定义的步骤进行实施。

## 3. 最终实施方案

### [ ] 第一步：修改 ANTLR 语法 (`PenguinLang.g4`)

**任务**: 废弃 `<...>` 语法，引入 `#template(...)` 规则。

**位置**: `PenguinLangParser/PenguinLang.g4`

**伪代码**:

```antlr
// --- 修改前 ---

// 旧的泛型参数定义
genericDefinitions: '<' IDENTIFIER (',' IDENTIFIER)* '>';

// 旧的类声明规则
classDeclaration: 'class' IDENTIFIER (genericDefinitions)? '{' ... '}';


// --- 修改后 ---

// 1. 新增 templateParameter 规则
templateParameter: IDENTIFIER ':' type; // 例如 "T : type" 或 "N : i32"

// 2. 新增 templateDeclaration 规则
templateDeclaration: '#' 'template' '(' templateParameter (',' templateParameter)* ')';

// 3. 修改 classDeclaration 等规则，使用新的 templateDeclaration
classDeclaration:
    (templateDeclaration)? 'class' IDENTIFIER '{' ... '}'
    ;

interfaceDefinition:
    (templateDeclaration)? 'interface' IDENTIFIER '{' ... '}'
    ;

// 4. 删除不再使用的 genericDefinitions 规则
```

### [ ] 第二步：更新抽象语法树 (AST) 节点

**任务**: 创建新的 AST 节点以匹配新语法，并废弃旧的节点。

**位置**: `PenguinLangParser/SyntaxNodes/`

**伪代码**:

```csharp
// --- 新增文件：TemplateDeclarationNode.cs ---
// 代表 "#template(T: type, N: i32)"
public class TemplateDeclarationNode : AstNode {
    public List<TemplateParameterNode> Parameters { get; }
}

// --- 新增文件：TemplateParameterNode.cs ---
// 代表 "T: type"
public class TemplateParameterNode : AstNode {
    public Identifier Name { get; }
    public TypeNode ParameterType { get; }
}

// --- 修改文件：ClassDeclaration.cs ---
public class ClassDeclaration : AstNode {
    // public GenericDefinitions? Generics { get; } // [删除]
    public TemplateDeclarationNode? Template { get; } // [新增]
    // ... 其他成员
}

// --- 删除文件：GenericDefinitions.cs ---
```

### [ ] 第三步：调整语义分析

**任务**: 修改语义分析过程，使其能够处理新的 `TemplateDeclarationNode` AST 节点。

**位置**: `BabyPenguin/SemanticPass/`

**伪代码**:

```csharp
// 文件: BabyPenguin/SemanticPass/01_SemanticScoping.cs

// --- 修改前 ---
public override void Visit(ClassDeclaration node) {
    var classSymbol = new ClassSymbol(node.Name);
    if (node.Generics != null) {
        foreach (var genericParam in node.Generics.Parameters) {
            var genericTypeSymbol = new GenericTypePlaceholderSymbol(genericParam.Name);
            classSymbol.AddGenericParameter(genericTypeSymbol);
        }
    }
    // ...
}

// --- 修改后 ---
public override void Visit(ClassDeclaration node) {
    var classSymbol = new ClassSymbol(node.Name);
    if (node.Template != null) {
        foreach (var templateParam in node.Template.Parameters) {
            // 检查参数是类型参数还是值参数
            if (IsTypeParameter(templateParam.ParameterType)) {
                 var genericTypeSymbol = new GenericTypePlaceholderSymbol(templateParam.Name);
                 classSymbol.AddGenericParameter(genericTypeSymbol);
            } 
            else {
                // 未来可支持非类型参数
                var constValueSymbol = new ConstValuePlaceholderSymbol(
                    templateParam.Name, 
                    ResolveType(templateParam.ParameterType)
                );
                classSymbol.AddTemplateValueParameter(constValueSymbol);
            }
        }
    }
    // ...
}
```

### [ ] 第四步：更新内置库文件 (`builtin.penguin`)

**任务**: 更新 `builtin.penguin` 中所有泛型定义，以符合新语法。

**位置**: `BabyPenguin/builtin.penguin`

**伪代码**:

```penguin
# --- 修改前 ---
enum Option<T> {
    some: T,
    None,
}

# --- 修改后 ---
#template(T: type)
enum Option {
    some: T,
    None,
}
```

### [ ] 第五步：修改现有测试用例 (`BabyPenguin.Tests`)

**任务**: 更新所有使用了旧泛型语法的测试用例。

**位置**: `BabyPenguin.Tests/`

**伪代码**:

```csharp
// 文件: BabyPenguin.Tests/ClassTest.cs

// --- 修改前 ---
[Fact]
public void TestGenericClass()
{
    var source = @"
        class Box<T> { value: T; }
        initial {
            let b: Box<i32> = new Box<i32>(10);
            // ...
        }
    ";
    // ...
}

// --- 修改后 ---
[Fact]
public void TestTemplateClass()
{
    var source = @"
        #template(T: type)
        class Box { value: T; }
        initial {
            let b: Box<i32> = new Box<i32>(10);
            // ...
        }
    ";
    // ...
}
```

## 4. 验证

完成所有代码修改后，执行以下命令以确保整个项目能够成功构建并通过所有测试。

```bash
# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test
```
