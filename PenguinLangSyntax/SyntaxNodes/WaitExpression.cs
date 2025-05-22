namespace PenguinLangSyntax.SyntaxNodes
{

    public class WaitExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WaitExpressionContext context)
            {
                Expression = context.expression() != null ? Build<Expression>(walker, context.expression()).GetEffectiveExpression() : null;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.waitExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Expression { get; set; }

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

        public override string BuildText()
        {
            if (Expression == null)
                return "wait;";
            else
                return $"wait {Expression!.BuildText()}";
        }
    }
}