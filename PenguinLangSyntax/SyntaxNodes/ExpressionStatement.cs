namespace PenguinLangSyntax.SyntaxNodes
{

    public class ExpressionStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ExpressionStatementContext context)
            {
                Expression = Build<Expression>(walker, context.expression());
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Expression? Expression { get; private set; }
    }
}