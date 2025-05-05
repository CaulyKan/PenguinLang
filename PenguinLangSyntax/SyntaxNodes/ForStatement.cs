namespace PenguinLangSyntax.SyntaxNodes
{
    public class ForStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ForStatementContext context)
            {
                Declaration = Build<Declaration>(walker, context.declaration());
                Expression = Build<Expression>(walker, context.expression());
                BodyStatement = Build<Statement>(walker, context.statement());
            }
        }
        [ChildrenNode]
        public Declaration? Declaration { get; private set; }

        [ChildrenNode]
        public Expression? Expression { get; private set; }

        [ChildrenNode]
        public Statement? BodyStatement { get; private set; }
    }

}