namespace PenguinLangSyntax.SyntaxNodes
{

    public class LogicalAndExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<BitWiseOrExpression> SubExpressions { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is LogicalAndExpressionContext context)
            {
                SubExpressions = context.children.OfType<InclusiveOrExpressionContext>()
                   .Select(x => Build<BitWiseOrExpression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new LogicalOrExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}