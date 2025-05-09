namespace PenguinLangSyntax.SyntaxNodes
{

    public class SignalStatement : SyntaxNode
    {
        [ChildrenNode]
        public Expression? SignalExpression { get; set; }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.signalStatement(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

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