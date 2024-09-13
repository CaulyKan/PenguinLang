
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

    public enum TypeSpecifierEnum
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
        Other
    }

    public enum UnaryOperatorEnum
    {
        Deref,
        Ref,
        Plus,
        Minus,
        BitwiseNot,
        LogicalNot
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