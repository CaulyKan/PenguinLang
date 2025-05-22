namespace PenguinLangSyntax.SyntaxNodes
{

    public class YieldStatement : SyntaxNode
    {
        [ChildrenNode]
        public ISyntaxExpression? YieldExpression { get; private set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is YieldStatementContext context)
            {
                YieldExpression = context.expression() is not null ? Build<Expression>(walker, context.expression()).GetEffectiveExpression() : null;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.yieldStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildText()
        {
            if (YieldExpression == null)
            {
                return "yield";
            }

            return $"yield {YieldExpression.BuildText()};";
        }
    }
}