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
                RightHandSide = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.assignmentStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public IdentifierOrMemberAccess? LeftHandSide { get; private set; }

        [ChildrenNode]
        public ISyntaxExpression? RightHandSide { get; private set; }

        public AssignmentOperatorEnum AssignmentOperator { get; private set; }

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add(LeftHandSide!.BuildText());
            parts.Add(AssignmentOperator switch
            {
                AssignmentOperatorEnum.Assign => "=",
                AssignmentOperatorEnum.AddAssign => "+=",
                AssignmentOperatorEnum.SubtractAssign => "-=",
                AssignmentOperatorEnum.MultiplyAssign => "*=",
                AssignmentOperatorEnum.DivideAssign => "/=",
                AssignmentOperatorEnum.ModuloAssign => "%=",
                AssignmentOperatorEnum.BitwiseAndAssign => "&=",
                AssignmentOperatorEnum.BitwiseOrAssign => "|=",
                AssignmentOperatorEnum.BitwiseXorAssign => "^=",
                AssignmentOperatorEnum.LeftShiftAssign => "<<=",
                AssignmentOperatorEnum.RightShiftAssign => ">>=",
                _ => throw new NotImplementedException($"Invalid assignment operator: {AssignmentOperator}")
            });
            parts.Add(RightHandSide!.BuildText());
            parts.Add(";");
            return string.Join(" ", parts);
        }
    }
}