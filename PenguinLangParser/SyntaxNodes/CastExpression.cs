namespace PenguinLangParser.SyntaxNodes
{

    public class CastExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PrimaryExpressionContext context)
            {
                CastTypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                SubExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override string ToShortString() => "cast";

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.primaryExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? CastTypeSpecifier { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? SubExpression { get; set; }

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string BuildText()
        {
            return $"cast<{CastTypeSpecifier!.BuildText()}>({SubExpression!.BuildText()})";
        }
    }
}
