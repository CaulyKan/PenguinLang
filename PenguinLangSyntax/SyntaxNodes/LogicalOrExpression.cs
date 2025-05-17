namespace PenguinLangSyntax.SyntaxNodes
{

    public class LogicalOrExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<LogicalAndExpression> SubExpressions { get; set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is LogicalOrExpressionContext context)
            {
                SubExpressions = context.children.OfType<LogicalAndExpressionContext>()
                   .Select(x => Build<LogicalAndExpression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.logicalOrExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new Expression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpression = this,
            };
        }

        public override string BuildSourceText()
        {
            if (SubExpressions.Count == 1)
            {
                return SubExpressions[0].BuildSourceText();
            }

            return string.Join(" || ", SubExpressions.Select(e => e.BuildSourceText()));
        }
    }
}