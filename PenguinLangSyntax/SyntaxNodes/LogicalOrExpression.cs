namespace PenguinLangSyntax.SyntaxNodes
{

    public class LogicalOrExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<LogicalAndExpression> SubExpressions { get; set; } = [];

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
    }
}