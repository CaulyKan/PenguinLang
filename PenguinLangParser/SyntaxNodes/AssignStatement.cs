namespace PenguinLangParser.SyntaxNodes
{

    public class AssignmentStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is AssignmentStatementContext context)
            {
                LeftHandSide = Build<PostfixExpression>(walker, context.postfixExpression());
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
                    _ => throw new System.NotImplementedException($"Invalid assignment operator: {context.assignmentOperator().GetText()}"),
                };
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.assignmentStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public PostfixExpression? LeftHandSide { get; private set; }

        [ChildrenNode]
        public ISyntaxExpression? RightHandSide { get; private set; }

        [SexpValue]
        public AssignmentOperatorEnum AssignmentOperator { get; private set; }

        public override string ToShortString() => AssignmentOperator.ToString();

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
                _ => throw new NotImplementedException($"Invalid assignment operator: {AssignmentOperator}")
            });
            parts.Add(RightHandSide!.BuildText());
            parts.Add(";");
            return string.Join(" ", parts);
        }
    }
}
