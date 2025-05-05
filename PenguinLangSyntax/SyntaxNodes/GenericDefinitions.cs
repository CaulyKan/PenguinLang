namespace PenguinLangSyntax.SyntaxNodes
{

    public class GenericDefinitions : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is GenericDefinitionsContext context)
            {
                TypeParameters = context.children.OfType<IdentifierContext>()
                   .Select(x => Build<SymbolIdentifier>(walker, x) as Identifier)
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public List<Identifier> TypeParameters { get; private set; } = [];
    }
}