namespace PenguinLangParser.SyntaxNodes
{

    public class ExpressionStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ExpressionStatementContext context)
            {
                Expression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.expressionStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Expression { get; private set; }

        public override string ToShortString() => "";

        public override string BuildText()
        {
            return $"{Expression!.BuildText()};";
        }
    }
}