namespace PenguinLangSyntax.SyntaxNodes
{
    public class IfStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is IfStatementContext context)
            {
                Condition = Build<Expression>(walker, context.expression());
                var statements = context.children.OfType<StatementContext>().ToList();
                if (statements.Count == 1)
                {
                    MainStatement = Build<Statement>(walker, statements[0]);
                }
                else if (statements.Count == 2)
                {
                    MainStatement = Build<Statement>(walker, statements[0]);
                    ElseStatement = Build<Statement>(walker, statements[1]);
                }
                else
                {
                    throw new System.NotImplementedException("Invalid number of statements in if statement");
                }
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Expression? Condition { get; private set; }

        [ChildrenNode]
        public Statement? MainStatement { get; private set; }

        [ChildrenNode]
        public Statement? ElseStatement { get; private set; }

        public bool HasElse => ElseStatement is not null;
    }

}