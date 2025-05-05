namespace PenguinLangSyntax.SyntaxNodes
{

    public class BitWiseOrExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<BitwiseXorExpression> SubExpressions { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InclusiveOrExpressionContext context)
            {
                SubExpressions = context.children.OfType<ExclusiveOrExpressionContext>()
                   .Select(x => Build<BitwiseXorExpression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new LogicalAndExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}