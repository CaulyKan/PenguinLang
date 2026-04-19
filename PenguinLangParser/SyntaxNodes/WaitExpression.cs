namespace PenguinLangParser.SyntaxNodes
{

    public class WaitExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                Expression = context.expression().Length > 0 ? Build<Expression>(walker, context.expression(0)).GetEffectiveExpression() : null;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Expression { get; set; }

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

        public override string ToShortString() => "wait";

        public override string BuildText()
        {
            if (Expression == null)
                return "wait";
            else
                return $"wait {Expression!.BuildText()}";
        }
    }
}
