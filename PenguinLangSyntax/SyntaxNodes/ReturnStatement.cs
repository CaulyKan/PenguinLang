namespace PenguinLangSyntax.SyntaxNodes
{
    public class ReturnStatement : SyntaxNode
    {
        [ChildrenNode]
        public Expression? ReturnExpression { get; private set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ReturnStatementContext context)
            {
                ReturnExpression = context.expression() is not null ? Build<Expression>(walker, context.expression()) : null;
            }
            else throw new NotImplementedException();
        }
    }

}