namespace PenguinLangSyntax.SyntaxNodes
{

    public class Expression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public LogicalOrExpression? SubExpression { get; private set; }

        public bool IsSimple => SubExpression!.IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ExpressionContext context)
            {
                SubExpression = Build<LogicalOrExpression>(walker, context.logicalOrExpression());
            }
            else throw new NotImplementedException();
        }
    }
}