namespace PenguinLangSyntax.SyntaxNodes
{

    public class InterfaceImplementation : SyntaxNode, ISyntaxScope, IInterfaceImplementation
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InterfaceImplementationContext context)
            {
                walker.PushScope(SyntaxScopeType.Interface, this);

                InterfaceType = Build<TypeSpecifier>(walker, context.typeSpecifier());
                Functions = context.children.OfType<FunctionDefinitionContext>()
                   .Select(x => Build<FunctionDefinition>(walker, x))
                   .ToList();
                WhereDefinition = context.whereDefinition() != null ? Build<WhereDefinition>(walker, context.whereDefinition()) : null;

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public TypeSpecifier? InterfaceType { get; set; }

        public string Name => InterfaceType!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.InterfaceImplementation;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public bool IsAnonymous => false;

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; set; } = [];

        [ChildrenNode]
        public WhereDefinition? WhereDefinition { get; set; }
    }
}