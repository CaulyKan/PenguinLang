# 阶段一：解析器工具实施方案

本文档总结了将 `PenguinLangParser` 项目改造为AST序列化工具的最终实施方案。

## 1. 目标

将 `PenguinLangParser` 项目从一个库转变为一个功能完整的命令行工具。该工具能够解析指定的 Penguin 语言源文件，并将其生成的抽象语法树（AST）以 S-表达式（S-expression）的格式序列化到输出文件中，作为后续编译流程的第一步。

## 2. 限制

在方案讨论阶段，我们只进行分析和设计，不实际修改代码。所有实施将在本方案确认后，根据本文档的指导进行。

## 3. 最终实施方案

经过分析，我们发现项目的基础框架（如可执行文件配置、主程序入口、命令行参数解析）已基本完成，但核心的序列化逻辑 `SexpSerializer.cs` 非常初级且不通用。

因此，我们的核心任务是重写序列化逻辑，使其变得健壮、通用，并能自动适应任何 AST 节点。

---

### [x] 步骤一：引入 `[SexpValue]` 特性

为了在序列化时区分节点的“值属性”（如标识符的名称）和“子节点属性”，我们首先需要定义一个新的特性。

*   **操作**: 在 `PenguinLangParser/SyntaxNodes/SyntaxNode.cs` 文件中，或一个新文件中，添加以下特性定义。

```csharp
// PenguinLangParser/SyntaxNodes/SyntaxNode.cs

// ... (文件顶部 using 之下)

// 用于标记应作为“值”被序列化的属性
[AttributeUsage(AttributeTargets.Property)]
public class SexpValueAttribute : Attribute { }

// ... (文件其余部分)
```

---


### [x] 步骤二：重写 `SexpSerializer.cs`

此步骤旨在搭建一个通用的序列化器框架。注意：此版本的序列化器**尚未支持列表（List）序列化**，该功能将在后续步骤中添加。

*   **操作**: 将 `PenguinLangParser/SexpSerializer.cs` 的内容替换为以下代码。

```csharp
// PenguinLangParser/SexpSerializer.cs (重写版本)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using PenguinLangParser.SyntaxNodes;

namespace PenguinLangParser;

public class SexpSerializer
{
    public static string Serialize(IEnumerable<ISyntaxNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            SerializeNode(node, sb, 0);
            sb.AppendLine(); // 在顶层节点之间添加换行
        }
        return sb.ToString();
    }

    private static void SerializeNode(ISyntaxNode node, StringBuilder sb, int indent)
    {
        if (node == null)
        {
            sb.Append("null");
            return;
        }

        // 1. 添加缩进并开始 S-表达式: (
        sb.Append(new string(' ', indent));
        sb.Append("(");

        // 2. 写入节点类型名称
        sb.Append(node.GetType().Name);

        // 3. 查找并序列化所有标记为 [SexpValue] 的属性
        var valueProperties = GetValueProperties(node);
        foreach (var prop in valueProperties)
        {
            var propValue = prop.GetValue(node);
            if (propValue != null)
            {
                // 输出格式: (PropertyName "Value")
                sb.Append($" ({prop.Name} \"{Escape(propValue.ToString())}\")");
            }
        }

        // 4. 遍历由 ISyntaxNode.Children 定义的所有子节点
        var children = node.Children;
        if (children.Any())
        {
            foreach (var childPair in children)
            {
                sb.AppendLine(); // 换行以美化输出
                // 5. 对每个子节点进行递归调用
                SerializeNode(childPair.Value, sb, indent + 2);
            }
            // 将结束括号与子节点对齐
            sb.AppendLine();
            sb.Append(new string(' ', indent));
        }

        // 6. 结束当前节点的 S-表达式: )
        sb.Append(")");
    }

    // 辅助方法，通过反射获取所有标记了 [SexpValue] 的属性
    private static IEnumerable<PropertyInfo> GetValueProperties(ISyntaxNode node)
    {
        return node.GetType().GetProperties()
            .Where(p => Attribute.IsDefined(p, typeof(SexpValueAttribute)));
    }

    // 字符串转义辅助函数
    private static string Escape(string text)
    {
        if (text == null) return "";
        return text.Replace("\", "\\\\").Replace("\"", "\\"");
    }
}
```

---


### [x] 步骤三：在 AST 节点类中应用 `[SexpValue]` 特性

为了让新的序列化器工作，我们需要去修改现有的 AST 节点定义，为需要作为“值”输出的属性打上 `[SexpValue]` 标记。

*   **操作**: 系统性地检查 `PenguinLangParser/SyntaxNodes/` 目录下的所有节点类，为所有简单值属性（`string`, `bool`, `enum`）和列表属性（`List<T>`）添加 `[SexpValue]` 特性。
*   **注意**: `Declaration` 节点的 `Identifier` 属性是一个子节点，它应该已经被 `[ChildrenNode]` 标记，所以**不要**为它添加 `[SexpValue]`。我们只为那些本身是值的属性添加 `[SexpValue]`。

---


### [x] 步骤四：增强 `SexpSerializer` 以支持 `List<T>` 属性

此步骤将修改序列化器，使其能够正确处理被 `[SexpValue]` 标记的 `List<T>` 属性。

*   **操作**: 修改 `PenguinLangParser/SexpSerializer.cs` 文件中的 `SerializeNode` 方法，在处理 `valueProperties` 的循环中加入对列表类型的判断和处理逻辑。

```csharp
// SexpSerializer.cs 中 SerializeNode 方法需要修改的部分伪代码

// ... 在 SerializeNode 方法内部 ...
// 3. 查找并序列化所有标记为 [SexpValue] 的属性
var valueProperties = GetValueProperties(node);
foreach (var prop in valueProperties)
{
    var propValue = prop.GetValue(node);
    if (propValue == null) continue;

    sb.Append($" ({prop.Name} "); // 开始属性 S-表达式: (PropertyName 

    if (propValue is IList list && propValue is not string)
    {
        // --- 新增逻辑：如果属性值是一个列表 ---
        sb.Append("("); // 用括号包裹列表内容
        for(int i = 0; i < list.Count; i++)
        {
            sb.Append(Escape(list[i].ToString()));
            if (i < list.Count - 1) sb.Append(" "); // 用空格分隔元素
        }
        sb.Append(")");
    }
    else
    {
        // --- 原有逻辑：值是简单类型 ---
        sb.Append($"\"{Escape(propValue.ToString())}\"");
    }

    sb.Append(")"); // 结束属性 S-表达式: )
}
// ...
```

---


### [x] 步骤五：构建并测试

完成所有代码修改后，我们需要编译并运行工具，确保它能正确生成 S-表达式文件。

*   **操作**:
    1.  在终端中执行构建命令：
        ```bash
        dotnet build
        ```
    2.  使用一个测试文件（例如 `Examples/HelloWorld.penguin`）来运行工具：
        ```bash
        dotnet run --project PenguinLangParser -- --input Examples/HelloWorld.penguin --output helloworld.sexp
        ```
    3.  检查生成的 `helloworld.sexp` 文件内容是否符合预期，结构是否正确，所有节点和值是否都已包含。

```