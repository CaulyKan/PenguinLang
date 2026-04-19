using System.Collections.Generic;
using System.Linq;
using System.Text;
using PenguinLangParser.SyntaxNodes;

namespace PenguinLangParser
{
    /// <summary>
    /// Serializes SyntaxNode trees to S-expression format.
    /// </summary>
    public class SexpSerializer
    {
        /// <summary>
        /// Serializes a SyntaxNode to S-expression string.
        /// </summary>
        public static string Serialize(SyntaxNode node)
        {
            var sb = new StringBuilder();
            SerializeNode(node, sb, 0);
            return sb.ToString();
        }

        private static void SerializeNode(SyntaxNode node, StringBuilder sb, int indent)
        {
            sb.Append('(').Append(node.GetType().Name);

            // Output source location
            var loc = node.SourceLocation;
            sb.Append('\n').Append(new string(' ', indent + 2));
            sb.Append($"(:sourceFile \"{EscapeString(loc.FileName)}\" :startLine {loc.RowStart} :endLine {loc.RowEnd} :startCol {loc.ColStart} :endCol {loc.ColEnd})");

            // Output [SexpValue] properties
            var sexpValueProps = node.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(SexpValueAttribute), true).Length != 0);

            foreach (var prop in sexpValueProps)
            {
                var value = prop.GetValue(node);
                sb.Append('\n').Append(new string(' ', indent + 2));
                SerializeAttribute(prop.Name, value, sb);
            }

            // Output [ChildrenNode] properties with special handling for binary expressions
            SerializeChildren(node, sb, indent + 2);

            sb.Append(')');
        }

        private static void SerializeChildren(SyntaxNode node, StringBuilder sb, int indent)
        {
            // Special handling for binary expressions with interleave format
            if (node is AdditiveExpression additive)
            {
                SerializeBinaryExpressionInterleave(additive.SubExpressions, additive.Operators, sb, indent);
            }
            else if (node is MultiplicativeExpression multiplicative)
            {
                SerializeBinaryExpressionInterleave(multiplicative.SubExpressions, multiplicative.Operators, sb, indent);
            }
            else if (node is RelationalExpression relational)
            {
                SerializeBinaryExpressionInterleave(relational.SubExpressions, relational.Operators, sb, indent);
            }
            else if (node is EqualityExpression equality)
            {
                // EqualityExpression has a single operator
                SerializeEqualityExpression(equality, sb, indent);
            }
            else if (node is LogicalAndExpression logicalAnd)
            {
                SerializeBinaryExpressionImplicit(logicalAnd.SubExpressions, BinaryOperatorEnum.LogicalAnd, sb, indent);
            }
            else if (node is LogicalOrExpression logicalOr)
            {
                SerializeBinaryExpressionImplicit(logicalOr.SubExpressions, BinaryOperatorEnum.LogicalOr, sb, indent);
            }
            else if (node is BitwiseAndExpression bitwiseAnd)
            {
                SerializeBinaryExpressionImplicit(bitwiseAnd.SubExpressions, BinaryOperatorEnum.BitwiseAnd, sb, indent);
            }
            else if (node is BitWiseOrExpression bitwiseOr)
            {
                SerializeBinaryExpressionImplicit(bitwiseOr.SubExpressions, BinaryOperatorEnum.BitwiseOr, sb, indent);
            }
            else if (node is BitwiseXorExpression bitwiseXor)
            {
                SerializeBinaryExpressionImplicit(bitwiseXor.SubExpressions, BinaryOperatorEnum.BitwiseXor, sb, indent);
            }
            else
            {
                // Default handling: output children in order
                var childrenProps = node.GetType().GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(ChildrenNodeAttribute), true).Length != 0);

                foreach (var prop in childrenProps)
                {
                    var value = prop.GetValue(node);
                    if (value is IEnumerable<SyntaxNode> list)
                    {
                        foreach (var child in list.Where(c => c != null))
                        {
                            sb.Append('\n').Append(new string(' ', indent));
                            SerializeNode(child, sb, indent);
                        }
                    }
                    else if (value is IEnumerable<ISyntaxNode> list2)
                    {
                        foreach (var child in list2.Where(c => c != null).Cast<SyntaxNode>())
                        {
                            sb.Append('\n').Append(new string(' ', indent));
                            SerializeNode(child, sb, indent);
                        }
                    }
                    else if (value is SyntaxNode singleChild && singleChild != null)
                    {
                        sb.Append('\n').Append(new string(' ', indent));
                        SerializeNode(singleChild, sb, indent);
                    }
                }
            }
        }

        private static void SerializeBinaryExpressionInterleave(
            List<ISyntaxExpression> subExpressions,
            List<BinaryOperatorEnum> operators,
            StringBuilder sb, int indent)
        {
            for (int i = 0; i < subExpressions.Count; i++)
            {
                var expr = subExpressions[i] as SyntaxNode;
                if (expr != null)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    SerializeNode(expr, sb, indent);
                }

                if (i < operators.Count)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    sb.Append("(Operator ").Append(operators[i]).Append(')');
                }
            }
        }

        private static void SerializeEqualityExpression(EqualityExpression equality, StringBuilder sb, int indent)
        {
            // EqualityExpression uses single operator between sub-expressions
            for (int i = 0; i < equality.SubExpressions.Count; i++)
            {
                var expr = equality.SubExpressions[i] as SyntaxNode;
                if (expr != null)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    SerializeNode(expr, sb, indent);
                }

                if (i < equality.SubExpressions.Count - 1 && equality.Operator != null)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    sb.Append("(Operator ").Append(equality.Operator).Append(')');
                }
            }
        }

        private static void SerializeBinaryExpressionImplicit(
            List<ISyntaxExpression> subExpressions,
            BinaryOperatorEnum op,
            StringBuilder sb, int indent)
        {
            for (int i = 0; i < subExpressions.Count; i++)
            {
                var expr = subExpressions[i] as SyntaxNode;
                if (expr != null)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    SerializeNode(expr, sb, indent);
                }

                if (i < subExpressions.Count - 1)
                {
                    sb.Append('\n').Append(new string(' ', indent));
                    sb.Append("(Operator ").Append(op).Append(')');
                }
            }
        }

        private static void SerializeAttribute(string name, object? value, StringBuilder sb)
        {
            sb.Append("(:").Append(ToCamelCase(name)).Append(" ");
            SerializeValue(value, sb);
            sb.Append(')');
        }

        private static void SerializeValue(object? value, StringBuilder sb)
        {
            switch (value)
            {
                case null:
                    sb.Append("nil");
                    break;
                case string s:
                    sb.Append('"').Append(EscapeString(s)).Append('"');
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case Enum e:
                    sb.Append(e.ToString());
                    break;
                case int i:
                    sb.Append(i);
                    break;
                case long l:
                    sb.Append(l);
                    break;
                case float f:
                    sb.Append(f);
                    break;
                case double d:
                    sb.Append(d);
                    break;
                case IEnumerable<object> list:
                    sb.Append('(');
                    var first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(' ');
                        first = false;
                        SerializeValue(item, sb);
                    }
                    sb.Append(')');
                    break;
                default:
                    sb.Append('"').Append(EscapeString(value?.ToString() ?? "")).Append('"');
                    break;
            }
        }

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}
