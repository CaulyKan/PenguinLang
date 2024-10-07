
using System.Text.RegularExpressions;

namespace BabyPenguin
{
    public interface IPrettyPrint
    {
        static string PrintText(int indentLevel, string text) => new string(' ', indentLevel * 2) + text;

        IEnumerable<string> PrettyPrint(int indentLevel, string? prefix = null)
        {
            yield return new string(' ', indentLevel * 2) + (prefix ?? " ") + ToString();
        }
    }


    public partial record NameComponents(List<string> Prefix, string Name, List<string> Generics)
    {
        public string PrefixString => string.Join(".", Prefix);
        public string NameWithPrefix => string.IsNullOrEmpty(PrefixString) ? Name : PrefixString + "." + Name;
        public sealed override string ToString() => NameWithPrefix + (Generics.Count > 0 ? "<" + string.Join(",", Generics) + ">" : "");
        public static NameComponents ParseName(string name)
        {
            var list = SplitStringPreservingAngleBrackets(name, '.');
            var prefix = list.Take(list.Count - 1).ToList();
            var last = list.Last();
            var simpleName = last.Contains('<') ? last.Split('<')[0] : last;
            var generics = last.Contains('<') ? SplitStringPreservingAngleBrackets(last.Substring(simpleName.Length + 1, last.LastIndexOf('>') - simpleName.Length - 1), ',') : [];
            return new NameComponents(prefix, simpleName, generics);
        }

        public static List<string> SplitStringPreservingAngleBrackets(string input, char deli)
        {
            var resultList = new List<string>();
            int startIndex = 0;
            int bracketLevel = 0;

            for (int i = 0; i < input.Length; i++)
            {
                char currentChar = input[i];

                if (currentChar == '<')
                {
                    bracketLevel++;
                }
                else if (currentChar == '>')
                {
                    bracketLevel--;
                }
                else if (currentChar == deli && bracketLevel == 0)
                {
                    resultList.Add(input.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }

            // Add the last part of the string
            if (startIndex < input.Length)
            {
                resultList.Add(input.Substring(startIndex));
            }

            return resultList.Select(s => s.Trim()).ToList();
        }
    }

    public class Or<T1, T2>
    {
        public Or(T1 val)
        {
            IsLeft = true;
            Left = val;
        }
        public Or(T2 val)
        {
            IsLeft = false;
            Right = val;
        }
        public bool IsLeft { get; }
        public bool IsRight => !IsLeft;
        public T1? Left { get; }
        public T2? Right { get; }
        public static implicit operator Or<T1, T2>(T1 val) => new(val);
        public static implicit operator Or<T1, T2>(T2 val) => new(val);

        override public string? ToString() => IsLeft ? Left?.ToString() : Right?.ToString();
    }

    public enum TypeEnum
    {
        Void,
        U8,
        U16,
        U32,
        U64,
        I8,
        I16,
        I32,
        I64,
        Float,
        Double,
        String,
        Bool,
        Char,
        Fun,
        Class,
        Enum,
    }
    public enum BinaryOperatorEnum
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        LessThan,
        GreaterThan,
        LessThanOrEqual,
        GreaterThanOrEqual,
        Equal,
        NotEqual,
        LogicalAnd,
        LogicalOr,
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        LeftShift,
        RightShift,
        Is
    }

    public enum UnaryOperatorEnum
    {
        Deref,
        Ref,
        Plus,
        Minus,
        BitwiseNot,
        LogicalNot,
    }

    public enum AssignmentOperatorEnum
    {
        Assign,
        MultiplyAssign,
        DivideAssign,
        ModuloAssign,
        AddAssign,
        SubtractAssign,
        LeftShiftAssign,
        RightShiftAssign,
        BitwiseAndAssign,
        BitwiseOrAssign,
        BitwiseXorAssign
    }

}