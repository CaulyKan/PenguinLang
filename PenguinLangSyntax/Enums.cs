namespace PenguinLangSyntax
{

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

}