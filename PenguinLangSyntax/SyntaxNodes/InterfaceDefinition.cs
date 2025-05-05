namespace PenguinLangSyntax.SyntaxNodes
{

    public class InterfaceDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InterfaceDefinitionContext context)
            {
                walker.PushScope(SyntaxScopeType.Interface, this);

                InterfaceIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                Functions = context.children.OfType<FunctionDefinitionContext>()
                   .Select(x => Build<FunctionDefinition>(walker, x))
                   .ToList();
                GenericDefinitions = context.genericDefinitions() != null ? Build<GenericDefinitions>(walker, context.genericDefinitions()) : null;
                InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
                   .Select(x => Build<InterfaceImplementation>(walker, x))
                   .ToList();

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Identifier? InterfaceIdentifier { get; private set; }

        public string Name => InterfaceIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Interface;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; private set; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public GenericDefinitions? GenericDefinitions { get; private set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; private set; } = [];
    }
}