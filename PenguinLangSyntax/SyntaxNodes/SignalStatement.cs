namespace PenguinLangSyntax.SyntaxNodes
{

    public class SignalStatement : SyntaxNode
    {
        [ChildrenNode]
        public Expression? SignalExpression { get; set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is SignalStatementContext context)
            {
                SignalExpression = context.expression() is not null ? Build<Expression>(walker, context.expression()) : null;
            }
            else throw new NotImplementedException();
        }
    }
}