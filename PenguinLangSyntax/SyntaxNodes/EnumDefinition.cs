namespace PenguinLangSyntax.SyntaxNodes
{

    public class EnumDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EnumDefinitionContext context)
            {
                walker.PushScope(SyntaxScopeType.Enum, this);

                EnumIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                EnumDeclarations = context.children.OfType<EnumDeclarationContext>()
                   .Select(x => Build<EnumDeclaration>(walker, x))
                   .ToList();
                Functions = context.children.OfType<FunctionDefinitionContext>()
                   .Select(x => Build<FunctionDefinition>(walker, x))
                   .ToList();
                InitialRoutines = context.children.OfType<InitialRoutineContext>()
                                   .Select(x => Build<InitialRoutineDefinition>(walker, x))
                                   .ToList();
                GenericDefinitions = context.genericDefinitions() != null ? Build<GenericDefinitions>(walker, context.genericDefinitions()) : null;
                InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
                    .Select(x => Build<InterfaceImplementation>(walker, x))
                    .ToList();


                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.enumDefinition(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? EnumIdentifier { get; private set; }

        public string Name => EnumIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; private set; } = [];

        [ChildrenNode]
        public List<InitialRoutineDefinition> InitialRoutines { get; private set; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public List<EnumDeclaration> EnumDeclarations { get; private set; } = [];

        [ChildrenNode]
        public GenericDefinitions? GenericDefinitions { get; private set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; private set; } = [];
    }
}