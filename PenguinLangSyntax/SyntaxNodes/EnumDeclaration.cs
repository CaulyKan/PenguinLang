namespace PenguinLangSyntax.SyntaxNodes
{

    public class EnumDeclaration : SyntaxNode, ISyntaxScope
    {

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EnumDeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                TypeSpecifier = context.typeSpecifier() != null ? Build<TypeSpecifier>(walker, context.typeSpecifier()) : null;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.enumDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? Identifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        public string Name => Identifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Enum;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public bool IsAnonymous => false;

        public ISyntaxScope? ParentScope { get; set; }

        public override string BuildText()
        {
            if (TypeSpecifier == null)
                return Identifier!.BuildText();
            else
                return $"{Identifier!.BuildText()} : {TypeSpecifier!.BuildText()};";
        }

    }

}