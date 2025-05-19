namespace PenguinLangSyntax.SyntaxNodes
{

    public class EqualityExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public BinaryOperatorEnum? Operator { get; private set; }

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EqualityExpressionContext context)
            {
                SubExpressions = context.children.OfType<RelationalExpressionContext>()
                   .Select(x => Build<RelationalExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
                Operator = context.equalityOperator() is null ? null : context.equalityOperator().GetText() switch
                {
                    "==" => BinaryOperatorEnum.Equal,
                    "!=" => BinaryOperatorEnum.NotEqual,
                    "is" => BinaryOperatorEnum.Is,
                    _ => throw new System.NotImplementedException("Invalid equality operator"),
                };
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.equalityExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildSourceText()
        {
            if (SubExpressions.Count == 1)
            {
                return SubExpressions[0].BuildSourceText();
            }

            var parts = new List<string>();
            for (int i = 0; i < SubExpressions.Count; i++)
            {
                parts.Add(SubExpressions[i].BuildSourceText());
                if (i < SubExpressions.Count - 1)
                {
                    parts.Add(Operator switch
                    {
                        BinaryOperatorEnum.Equal => "==",
                        BinaryOperatorEnum.NotEqual => "!=",
                        BinaryOperatorEnum.Is => "is",
                        _ => throw new NotImplementedException("Invalid equality operator")
                    });
                }
            }
            return string.Join(" ", parts);
        }
    }
}