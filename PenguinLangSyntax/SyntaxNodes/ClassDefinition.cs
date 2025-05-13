namespace PenguinLangSyntax.SyntaxNodes
{

    public class ClassDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ClassDefinitionContext context)
            {
                walker.PushScope(SyntaxScopeType.Class, this);

                ClassIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                ClassDeclarations = context.children.OfType<ClassDeclarationContext>()
                   .Select(x => Build<ClassDeclaration>(walker, x))
                   .ToList();
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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.classDefinition(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? ClassIdentifier { get; private set; }

        public string Name => ClassIdentifier!.Name;

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
        public List<ClassDeclaration> ClassDeclarations { get; private set; } = [];

        [ChildrenNode]
        public GenericDefinitions? GenericDefinitions { get; private set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; private set; } = [];

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add("class");
            parts.Add(ClassIdentifier!.BuildSourceText());
            if (GenericDefinitions != null)
            {
                parts.Add(GenericDefinitions.BuildSourceText());
            }
            parts.Add("{");
            if (InterfaceImplementations.Count > 0)
            {
                parts.Add(string.Join(", ", InterfaceImplementations.Select(impl => impl.BuildSourceText())));
            }
            if (ClassDeclarations.Count > 0)
            {
                parts.Add(string.Join("\n", ClassDeclarations.Select(decl => decl.BuildSourceText())));
            }
            if (Functions.Count > 0)
            {
                parts.Add(string.Join("\n", Functions.Select(func => func.BuildSourceText())));
            }
            if (InitialRoutines.Count > 0)
            {
                parts.Add(string.Join("\n", InitialRoutines.Select(routine => routine.BuildSourceText())));
            }
            parts.Add("}");
            return string.Join(" ", parts);
        }
    }
}