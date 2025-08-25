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
                Declarations = context.children.OfType<ClassDeclarationContext>()
                   .Select(x => Build<ClassDeclaration>(walker, x))
                   .ToList();
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

                OnRoutines = context.children.OfType<OnRoutineContext>()
                    .Select(x => Build<OnRoutineDefinition>(walker, x))
                    .ToList();
                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.classDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? ClassIdentifier { get; set; }

        public string Name => ClassIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; set; } = [];

        [ChildrenNode]
        public List<EventDefinition> Events { get; set; } = [];

        [ChildrenNode]
        public List<OnRoutineDefinition> OnRoutines { get; set; } = [];

        [ChildrenNode]
        public List<InitialRoutineDefinition> InitialRoutines { get; set; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public List<ClassDeclaration> Declarations { get; set; } = [];

        [ChildrenNode]
        public GenericDefinitions? GenericDefinitions { get; set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; set; } = [];

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("class");
            parts.Add(ClassIdentifier!.BuildText());
            if (GenericDefinitions != null)
            {
                parts.Add(GenericDefinitions.BuildText());
            }
            parts.Add("{\n");
            if (Events.Count > 0)
            {
                parts.Add(string.Join("\n", Events.Select(i => i.BuildText())));
            }
            if (OnRoutines.Count > 0)
            {
                parts.Add(string.Join("\n", OnRoutines.Select(i => i.BuildText())));
            }
            if (InterfaceImplementations.Count > 0)
            {
                parts.Add(string.Join(", ", InterfaceImplementations.Select(impl => impl.BuildText())));
            }
            if (Declarations.Count > 0)
            {
                parts.Add(string.Join("\n", Declarations.Select(decl => decl.BuildText())));
            }
            if (Functions.Count > 0)
            {
                parts.Add(string.Join("\n", Functions.Select(func => func.BuildText())));
            }
            if (InitialRoutines.Count > 0)
            {
                parts.Add(string.Join("\n", InitialRoutines.Select(routine => routine.BuildText())));
            }
            parts.Add("}\n");
            return string.Join(" ", parts);
        }
    }
}