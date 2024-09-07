
namespace BabyPenguin
{
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