namespace PenguinLangSyntax.SyntaxNodes
{

    public class NewExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is NewExpressionContext context)
            {
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        [ChildrenNode]
        public List<Expression> ArgumentsExpression { get; private set; } = [];

        public bool IsSimple => false;
    }
}