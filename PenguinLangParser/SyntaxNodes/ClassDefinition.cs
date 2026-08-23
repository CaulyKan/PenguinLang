namespace PenguinLangParser.SyntaxNodes
{

    public class ClassDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ClassDefinitionContext context)
            {
                walker.PushScope(SyntaxScopeType.Class, this);

                // Optional template declaration before class keyword
                Template = context.templateDeclaration() != null ? Build<TemplateDeclaration>(walker, context.templateDeclaration()) : null;
                ClassIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                Declarations = context.children.OfType<ClassDeclarationContext>()
                   .Select(x => Build<ClassDeclaration>(walker, x))
                   .ToList();
                Functions = context.children.OfType<FunctionDefinitionContext>()
                   .Select(x => Build<FunctionDefinition>(walker, x))
                   .ToList();
                // GenericDefinitions removed in favor of TemplateDeclaration
                InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
                   .Select(x => Build<InterfaceImplementation>(walker, x))
                   .ToList();
                Ports = context.children.OfType<PortDeclarationContext>()
                   .Select(x => Build<PortDefinition>(walker, x))
                   .ToList();
                InitialRoutines = context.children.OfType<InitialRoutineContext>()
                   .Select(x => Build<InitialRoutineDefinition>(walker, x))
                   .ToList();
                Constructs = context.children.OfType<ConstructBlockContext>()
                   .Select(x => Build<ConstructDefinition>(walker, x))
                   .ToList();
                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.classDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? ClassIdentifier { get; set; }

        public override string ToShortString() => ClassIdentifier!.BuildText();

        public string Name => ClassIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; set; } = [];

        [ChildrenNode]
        public List<PortDefinition> Ports { get; set; } = [];

        [ChildrenNode]
        public List<InitialRoutineDefinition> InitialRoutines { get; set; } = [];

        [ChildrenNode]
        public List<ConstructDefinition> Constructs { get; set; } = [];

        [SexpValue]
        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [ChildrenNode]
        public List<ClassDeclaration> Declarations { get; set; } = [];

        [ChildrenNode]
        public TemplateDeclaration? Template { get; set; } = null;

        [ChildrenNode]
        public List<InterfaceImplementation> InterfaceImplementations { get; set; } = [];

        public override string BuildText()
        {
            var parts = new List<string>();
            if (Template != null)
            {
                parts.Add(Template.BuildText());
            }
            parts.Add("class");
            parts.Add(ClassIdentifier!.BuildText());
            parts.Add("{\n");
            if (InterfaceImplementations.Count > 0)
            {
                parts.Add(string.Join(", ", InterfaceImplementations.Select(impl => impl.BuildText())));
            }
            if (Declarations.Count > 0)
            {
                parts.Add(string.Join("\n", Declarations.Select(decl => decl.BuildText())));
            }
            if (Ports.Count > 0)
            {
                parts.Add(string.Join("\n", Ports.Select(port => port.BuildText())));
            }
            if (Functions.Count > 0)
            {
                parts.Add(string.Join("\n", Functions.Select(func => func.BuildText())));
            }
            if (InitialRoutines.Count > 0)
            {
                parts.Add(string.Join("\n", InitialRoutines.Select(routine => routine.BuildText())));
            }
            if (Constructs.Count > 0)
            {
                parts.Add(string.Join("\n", Constructs.Select(c => c.BuildText())));
            }
            parts.Add("}\n");
            return string.Join(" ", parts);
        }
    }
}
