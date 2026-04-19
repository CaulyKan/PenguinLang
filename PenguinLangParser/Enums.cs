namespace PenguinLangParser
{

    public enum AssignmentOperatorEnum
    {
        Assign,
        MultiplyAssign,
        DivideAssign,
        ModuloAssign,
        AddAssign,
        SubtractAssign,
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

    public enum Mutability
    {
        Unspecified,
        Auto,
        Immutable,
        Mutable,
    }
}