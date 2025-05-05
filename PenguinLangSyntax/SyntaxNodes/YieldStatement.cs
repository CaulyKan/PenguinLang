namespace PenguinLangSyntax.SyntaxNodes
{

    public class YieldStatement : SyntaxNode
    {
        [ChildrenNode]
        public Expression? YieldExpression { get; private set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is YieldStatementContext context)
            {
                YieldExpression = context.expression() is not null ? Build<Expression>(walker, context.expression()) : null;
            }
            else throw new NotImplementedException();
        }
    }
}