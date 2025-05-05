namespace PenguinLangSyntax.SyntaxNodes
{

    public class EqualityExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<RelationalExpression> SubExpressions { get; private set; } = [];

        public BinaryOperatorEnum? Operator { get; private set; }

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

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
    }
}