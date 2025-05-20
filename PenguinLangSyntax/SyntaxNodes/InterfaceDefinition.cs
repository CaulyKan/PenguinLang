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
                Events = context.children.OfType<EventDefinitionContext>()
                    .Select(x => Build<EventDefinition>(walker, x))
                    .ToList();

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.interfaceDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? InterfaceIdentifier { get; private set; }

        public string Name => InterfaceIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Interface;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        [ChildrenNode]
        public List<EventDefinition> Events { get; set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; private set; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public GenericDefinitions? GenericDefinitions { get; private set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; private set; } = [];

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add("interface");
            parts.Add(InterfaceIdentifier!.BuildSourceText());

            if (GenericDefinitions != null)
            {
                parts.Add(GenericDefinitions.BuildSourceText());
            }

            parts.Add("{");
            if (Events.Count > 0)
            {
                parts.Add(string.Join("\n", Events.Select(i => i.BuildSourceText())));
            }

            foreach (var function in Functions)
            {
                parts.Add(function.BuildSourceText());
            }

            foreach (var impl in InterfaceImplementations)
            {
                parts.Add(impl.BuildSourceText());
            }

            parts.Add("}");

            return string.Join(" ", parts);
        }
    }
}