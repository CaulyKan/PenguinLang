namespace PenguinLangSyntax.SyntaxNodes
{

    public class MultiplicativeExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<CastExpression> SubExpressions { get; set; } = [];

        public List<BinaryOperatorEnum> Operators { get; private set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.multiplicativeExpression(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is MultiplicativeExpressionContext context)
            {
                SubExpressions = context.children.OfType<CastExpressionContext>()
                   .Select(x => Build<CastExpression>(walker, x))
                   .ToList();
                Operators = context.multiplicativeOperator().Select(x => x.GetText() switch
                    {
                        "*" => BinaryOperatorEnum.Multiply,
                        "/" => BinaryOperatorEnum.Divide,
                        "%" => BinaryOperatorEnum.Modulo,
                        _ => throw new System.NotImplementedException("Invalid multiplicative operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new AdditiveExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}