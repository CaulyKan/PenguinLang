namespace PenguinLangSyntax.SyntaxNodes
{

    public class Expression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public ISyntaxExpression? SubExpression { get; set; }

        public bool IsSimple => SubExpression!.IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => (SubExpression as ISyntaxExpression)!.GetEffectiveExpression();

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ExpressionContext context)
            {
                SubExpression = Build<LogicalOrExpression>(walker, context.logicalOrExpression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.expression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildText()
        {
            return SubExpression!.BuildText();
        }
    }
}