namespace PenguinLangSyntax.SyntaxNodes
{
    public class BitwiseXorExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<BitwiseAndExpression> SubExpressions { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ExclusiveOrExpressionContext context)
            {
                SubExpressions = context.children.OfType<AndExpressionContext>()
                   .Select(x => Build<BitwiseAndExpression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new BitWiseOrExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}