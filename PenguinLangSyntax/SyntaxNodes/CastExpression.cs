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

        [ChildrenNode]
        public TypeSpecifier? CastTypeSpecifier { get; private set; }

        [ChildrenNode]
        public UnaryExpression? SubUnaryExpression { get; private set; }

        public bool IsTypeCast => CastTypeSpecifier is not null;

        public bool IsSimple => !IsTypeCast && SubUnaryExpression!.IsSimple;
    }
}