namespace PenguinLangSyntax.SyntaxNodes
{

    public class EqualityExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<RelationalExpression> SubExpressions { get; set; } = [];

        public BinaryOperatorEnum? Operator { get; private set; }

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EqualityExpressionContext context)
            {
                SubExpressions = context.children.OfType<RelationalExpressionContext>()
                   .Select(x => Build<RelationalExpression>(walker, x))
                   .ToList();
                Operator = context.equalityOperator() is null ? null : context.equalityOperator().GetText() switch
                {
                    "==" => BinaryOperatorEnum.Equal,
                    "!=" => BinaryOperatorEnum.NotEqual,
                    "is" => BinaryOperatorEnum.Is,
                    _ => throw new System.NotImplementedException("Invalid equality operator"),
                };
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.equalityExpression(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new BitwiseAndExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}