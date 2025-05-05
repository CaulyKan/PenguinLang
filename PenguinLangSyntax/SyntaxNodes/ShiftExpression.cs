namespace PenguinLangSyntax.SyntaxNodes
{

    public class ShiftExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<AdditiveExpression> SubExpressions { get; set; } = [];

        public List<BinaryOperatorEnum> Operators { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ShiftExpressionContext context)
            {
                SubExpressions = context.children.OfType<AdditiveExpressionContext>()
                   .Select(x => Build<AdditiveExpression>(walker, x))
                   .ToList();
                Operators = context.shiftOperator().Select(x => x.GetText() switch
                    {
                        "<<" => BinaryOperatorEnum.LeftShift,
                        ">>" => BinaryOperatorEnum.RightShift,
                        _ => throw new System.NotImplementedException("Invalid shift operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new RelationalExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpressions = [this],
            };
        }
    }
}