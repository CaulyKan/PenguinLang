namespace PenguinLangSyntax.SyntaxNodes
{

    public class TypeReferenceDeclaration : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TypeReferenceDeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.typeReferenceDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? Identifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        public string Name => Identifier!.Name;

        public override string BuildSourceText()
        {
            return $"type {Identifier!.BuildSourceText()} = {TypeSpecifier!.BuildSourceText()}";
        }
    }
}