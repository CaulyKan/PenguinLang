namespace PenguinLangSyntax.SyntaxNodes
{
    public class ClassDeclaration : SyntaxNode, ISyntaxScope
    {
        [ChildrenNode]
        public Identifier? Identifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        [ChildrenNode]
        public Expression? Initializer { get; private set; }

        public string Name => Identifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public bool IsAnonymous => false;

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsReadonly { get; private set; }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.classDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ClassDeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                Initializer = context.expression() != null ? Build<Expression>(walker, context.expression()) : null;
                IsReadonly = context.declarationKeyword().GetText() == "val";
            }
            else throw new NotImplementedException();
        }

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add(IsReadonly ? "val" : "var");
            parts.Add(Identifier!.BuildSourceText());
            parts.Add(":");
            parts.Add(TypeSpecifier!.BuildSourceText());
            if (Initializer != null)
            {
                parts.Add("=");
                parts.Add(Initializer.BuildSourceText());
            }
            parts.Add(";");
            return string.Join(" ", parts);
        }
    }
}