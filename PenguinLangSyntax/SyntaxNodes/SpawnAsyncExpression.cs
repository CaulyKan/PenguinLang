namespace PenguinLangSyntax.SyntaxNodes
{

    public class SpawnAsyncExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is SpawnExpressionContext context)
            {
                Expression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.spawnExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;

        [ChildrenNode]
        public ISyntaxExpression? Expression { get; set; }

        public bool IsSimple => false;

        public override string BuildSourceText()
        {
            return $"async {Expression!.BuildSourceText()}";
        }
    }
}