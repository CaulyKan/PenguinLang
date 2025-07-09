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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.forStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Declaration? Declaration { get; private set; }

        [ChildrenNode]
        public Expression? Expression { get; private set; }

        [ChildrenNode]
        public Statement? BodyStatement { get; private set; }

        public override string BuildText()
        {
            return $"for (let {Declaration!.BuildText()} in {Expression!.BuildText()}) {BodyStatement!.BuildText()}";
        }
    }
}