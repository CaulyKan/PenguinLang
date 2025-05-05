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
    }
}