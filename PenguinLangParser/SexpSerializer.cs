using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using PenguinLangParser.SyntaxNodes;

namespace PenguinLangParser;

public class SExpSerializer
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
            if (propValue == null) continue;

            sb.Append($" ({prop.Name} "); // 开始属性 S-表达式: (PropertyName 

            if (propValue is IList list && propValue is not string)
            {
                // --- 新增逻辑：如果属性值是一个列表 --- 
                sb.Append("("); // 用括号包裹列表内容
                for (int i = 0; i < list.Count; i++)
                {
                    sb.Append(Escape(list[i].ToString()));
                    if (i < list.Count - 1) sb.Append(" "); // 用空格分隔元素
                }
                sb.Append(")");
            }
            else
            {
                // --- 修正后的逻辑：值是简单类型 ---
                sb.Append("\"" + Escape(propValue.ToString()) + "\"");
            }

            sb.Append(")"); // 结束属性 S-表达式: )
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
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}