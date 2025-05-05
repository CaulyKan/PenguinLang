namespace PenguinLangSyntax.SyntaxNodes
{

    public class GenericArguments : SyntaxNode
    {
        [ChildrenNode]
        public List<TypeSpecifier> TypeParameters { get; private set; } = [];

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is GenericArgumentsContext context)
            {
                TypeParameters = context.children.OfType<TypeSpecifierContext>()
                   .Select(x => Build<TypeSpecifier>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }
    }
}