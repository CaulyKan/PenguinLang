namespace PenguinLangParser.SyntaxNodes
{

    public class FunctionCallExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                Callee = Build<PostfixExpression>(walker, context.postfixExpression()).GetEffectiveExpression();
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x).GetEffectiveExpression())
                   .ToList();
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
        public ISyntaxExpression? Callee { get; set; }

        [ChildrenNode]
        public List<ISyntaxExpression> ArgumentsExpression { get; private set; } = [];

        public bool IsSimple => false;

        public override string ToShortString() => "";

        public override string BuildText()
        {
            var args = ArgumentsExpression.Count > 0
                ? string.Join(", ", ArgumentsExpression.Select(e => e.BuildText()))
                : "";
            return $"{Callee!.BuildText()}({args})";
        }
    }
}
