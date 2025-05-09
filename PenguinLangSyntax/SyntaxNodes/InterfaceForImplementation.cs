namespace PenguinLangSyntax.SyntaxNodes
{

    public class InterfaceForImplementation : SyntaxNode, ISyntaxScope, IInterfaceImplementation
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InterfaceForImplementationContext context)
            {
                walker.PushScope(SyntaxScopeType.Interface, this);

                InterfaceType = Build<TypeSpecifier>(walker, context.typeSpecifier()[0]);
                ForType = Build<TypeSpecifier>(walker, context.typeSpecifier()[1]);
                Functions = context.children.OfType<FunctionDefinitionContext>()
                   .Select(x => Build<FunctionDefinition>(walker, x))
                   .ToList();
                WhereDefinition = context.whereDefinition() != null ? Build<WhereDefinition>(walker, context.whereDefinition()) : null;

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.interfaceForImplementation(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? InterfaceType { get; set; }

        [ChildrenNode]
        public TypeSpecifier? ForType { get; set; }

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