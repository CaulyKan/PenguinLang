namespace PenguinLangSyntax.SyntaxNodes
{

    public class AdditiveExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.additiveExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public List<BinaryOperatorEnum> Operators { get; private set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is AdditiveExpressionContext context)
            {
                SubExpressions = context.children.OfType<MultiplicativeExpressionContext>()
                   .Select(x => Build<MultiplicativeExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
                Operators = context.additiveOperator().Select(x => x.GetText() switch
                    {
                        "+" => BinaryOperatorEnum.Add,
                        "-" => BinaryOperatorEnum.Subtract,
                        _ => throw new NotImplementedException("Invalid additive operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }

        public override string BuildText()
        {
            return string.Join("+", SubExpressions.Select(x => x.BuildText()));
        }
    }
}