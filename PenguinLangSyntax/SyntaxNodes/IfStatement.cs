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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.ifStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Expression? Condition { get; private set; }

        [ChildrenNode]
        public Statement? MainStatement { get; private set; }

        [ChildrenNode]
        public Statement? ElseStatement { get; private set; }

        public bool HasElse => ElseStatement is not null;

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add("if");
            parts.Add("(" + Condition!.BuildSourceText() + ")");
            parts.Add(MainStatement!.BuildSourceText());

            if (HasElse)
            {
                parts.Add("else");
                parts.Add(ElseStatement!.BuildSourceText());
            }

            return string.Join(" ", parts);
        }
    }
}