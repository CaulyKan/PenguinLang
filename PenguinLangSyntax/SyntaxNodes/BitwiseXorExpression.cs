namespace PenguinLangSyntax.SyntaxNodes
{
    public class BitwiseXorExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.bitwiseXorExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is BitwiseXorExpressionContext context)
            {
                SubExpressions = context.children.OfType<BitwiseAndExpressionContext>()
                   .Select(x => Build<BitwiseAndExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override string BuildText()
        {
            return string.Join(" ^ ", SubExpressions.Select(x => x.BuildText()));
        }
    }
}