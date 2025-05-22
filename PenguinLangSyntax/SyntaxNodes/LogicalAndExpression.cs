namespace PenguinLangSyntax.SyntaxNodes
{

    public class LogicalAndExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is LogicalAndExpressionContext context)
            {
                SubExpressions = context.children.OfType<BitwiseOrExpressionContext>()
                   .Select(x => Build<BitWiseOrExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.logicalAndExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildText()
        {
            if (SubExpressions.Count == 1)
            {
                return SubExpressions[0].BuildText();
            }

            return string.Join(" && ", SubExpressions.Select(e => e.BuildText()));
        }
    }
}