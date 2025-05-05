namespace PenguinLangSyntax.SyntaxNodes
{

    public class AssignmentStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is AssignmentStatementContext context)
            {
                LeftHandSide = Build<IdentifierOrMemberAccess>(walker, context.identifierOrMemberAccess());
                RightHandSide = Build<Expression>(walker, context.expression());
                AssignmentOperator = context.assignmentOperator().GetText() switch
                {
                    "=" => AssignmentOperatorEnum.Assign,
                    "+=" => AssignmentOperatorEnum.AddAssign,
                    "-=" => AssignmentOperatorEnum.SubtractAssign,
                    "*=" => AssignmentOperatorEnum.MultiplyAssign,
                    "/=" => AssignmentOperatorEnum.DivideAssign,
                    "%=" => AssignmentOperatorEnum.ModuloAssign,
                    "&=" => AssignmentOperatorEnum.BitwiseAndAssign,
                    "|=" => AssignmentOperatorEnum.BitwiseOrAssign,
                    "^=" => AssignmentOperatorEnum.BitwiseXorAssign,
                    "<<=" => AssignmentOperatorEnum.LeftShiftAssign,
                    ">>=" => AssignmentOperatorEnum.RightShiftAssign,
                    _ => throw new System.NotImplementedException($"Invalid assignment operator: {context.assignmentOperator().GetText()}"),
                };
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public IdentifierOrMemberAccess? LeftHandSide { get; private set; }

        [ChildrenNode]
        public Expression? RightHandSide { get; private set; }

        public AssignmentOperatorEnum AssignmentOperator { get; private set; }
    }
}