namespace PenguinLangSyntax.SyntaxNodes
{

    public class UnaryExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is UnaryExpressionContext context)
            {
                if (context.children.OfType<UnaryOperatorContext>().Any())
                {
                    UnaryOperator = context.unaryOperator().GetText() switch
                    {
                        "&" => (UnaryOperatorEnum?)UnaryOperatorEnum.Ref,
                        "*" => (UnaryOperatorEnum?)UnaryOperatorEnum.Deref,
                        "+" => (UnaryOperatorEnum?)UnaryOperatorEnum.Plus,
                        "-" => (UnaryOperatorEnum?)UnaryOperatorEnum.Minus,
                        "!" => (UnaryOperatorEnum?)UnaryOperatorEnum.LogicalNot,
                        "~" => (UnaryOperatorEnum?)UnaryOperatorEnum.BitwiseNot,
                        _ => throw new System.NotImplementedException("Invalid unary operator"),
                    };
                }
                SubExpression = Build<PostfixExpression>(walker, context.postfixExpression());
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public PostfixExpression? SubExpression { get; private set; }

        public UnaryOperatorEnum? UnaryOperator { get; private set; }

        public bool HasUnaryOperator => UnaryOperator is not null;

        public bool IsSimple => !HasUnaryOperator && SubExpression!.IsSimple;
    }
}