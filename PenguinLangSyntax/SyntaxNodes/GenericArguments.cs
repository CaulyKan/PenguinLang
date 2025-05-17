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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.genericArguments(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildSourceText()
        {
            if (TypeParameters.Count == 0)
            {
                return "";
            }

            return $"<{string.Join(", ", TypeParameters.Select(p => p.BuildSourceText()))}>";
        }
    }
}