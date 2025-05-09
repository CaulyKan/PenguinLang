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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.genericDefinitions(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public List<Identifier> TypeParameters { get; private set; } = [];
    }
}