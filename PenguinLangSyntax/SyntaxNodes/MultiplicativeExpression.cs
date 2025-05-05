namespace PenguinLangSyntax.SyntaxNodes
{

    public class MultiplicativeExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<CastExpression> SubExpressions { get; private set; } = [];

        public List<BinaryOperatorEnum> Operators { get; private set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

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
    }
}