namespace PenguinLangSyntax.SyntaxNodes
{

    public class CastExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is CastExpressionContext context)
            {
                if (context.typeSpecifier() != null)
                {
                    SubUnaryExpression = Build<UnaryExpression>(walker, context.unaryExpression()).GetEffectiveExpression();
                    CastTypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }
                else
                {
                    SubUnaryExpression = Build<UnaryExpression>(walker, context.unaryExpression());
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.castExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? CastTypeSpecifier { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? SubUnaryExpression { get; set; }

        public ISyntaxExpression GetEffectiveExpression() => IsTypeCast ? this : (SubUnaryExpression as ISyntaxExpression)!.GetEffectiveExpression();

        public bool IsTypeCast => CastTypeSpecifier is not null;

        public bool IsSimple => !IsTypeCast && SubUnaryExpression!.IsSimple;

        public override string BuildText()
        {
            if (!IsTypeCast)
            {
                return SubUnaryExpression!.BuildText();
            }
            return $"{SubUnaryExpression!.BuildText()} as {CastTypeSpecifier!.BuildText()}";
        }
    }
}