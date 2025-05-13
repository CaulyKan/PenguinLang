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
                    SubUnaryExpression = Build<UnaryExpression>(walker, context.unaryExpression());
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
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.castExpression(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? CastTypeSpecifier { get; set; }

        [ChildrenNode]
        public UnaryExpression? SubUnaryExpression { get; set; }

        public ISyntaxExpression GetEffectiveExpression() => IsTypeCast ? this : (SubUnaryExpression as ISyntaxExpression)!.GetEffectiveExpression();

        public bool IsTypeCast => CastTypeSpecifier is not null;

        public bool IsSimple => !IsTypeCast && SubUnaryExpression!.IsSimple;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new MultiplicativeExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }

        public override string BuildSourceText()
        {
            if (!IsTypeCast)
            {
                return SubUnaryExpression!.BuildSourceText();
            }
            return $"{SubUnaryExpression!.BuildSourceText()} as {CastTypeSpecifier!.BuildSourceText()}";
        }
    }
}