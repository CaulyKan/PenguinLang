namespace PenguinLangParser.SyntaxNodes
{

    public class SpawnAsyncExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                Expression = Build<Expression>(walker, context.expression(0)).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;

        [ChildrenNode]
        public ISyntaxExpression? Expression { get; set; }

        public bool IsSimple => false;

        public override string ToShortString() => "async";

        public override string BuildText()
        {
            return $"async {Expression!.BuildText()}";
        }
    }
}
