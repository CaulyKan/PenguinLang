namespace PenguinLangSyntax.SyntaxNodes
{

    public class LogicalAndExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<BitWiseOrExpression> SubExpressions { get; private set; } = [];

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
    }
}