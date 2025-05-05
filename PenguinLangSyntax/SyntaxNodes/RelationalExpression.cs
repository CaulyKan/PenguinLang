namespace PenguinLangSyntax.SyntaxNodes
{

    public class RelationalExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ShiftExpression> SubExpressions { get; private set; } = [];

        public List<BinaryOperatorEnum> Operators { get; private set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is RelationalExpressionContext context)
            {
                SubExpressions = context.children.OfType<ShiftExpressionContext>()
                   .Select(x => Build<ShiftExpression>(walker, x))
                   .ToList();
                Operators = context.relationalOperator().Select(x => x.GetText() switch
                    {
                        "<" => BinaryOperatorEnum.LessThan,
                        ">" => BinaryOperatorEnum.GreaterThan,
                        "<=" => BinaryOperatorEnum.LessThanOrEqual,
                        ">=" => BinaryOperatorEnum.GreaterThanOrEqual,
                        _ => throw new System.NotImplementedException("Invalid relational operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }
    }
}