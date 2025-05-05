namespace PenguinLangSyntax.SyntaxNodes
{
    public class WhileStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WhileStatementContext context)
            {
                Condition = Build<Expression>(walker, context.expression());
                BodyStatement = Build<Statement>(walker, context.statement());
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Expression? Condition { get; private set; }

        [ChildrenNode]
        public Statement? BodyStatement { get; private set; }
    }
}