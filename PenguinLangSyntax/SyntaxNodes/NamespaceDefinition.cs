
namespace PenguinLangSyntax.SyntaxNodes
{
    public class NamespaceDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext parserContext)
        {
            base.Build(walker, parserContext);

            if (parserContext is NamespaceDefinitionContext context)
            {
                Name = context.identifier().GetText();
                IsAnonymous = false;

                walker.PushScope(SyntaxScopeType.Namespace, this);

                foreach (var namespaceDeclarationContext in context.namespaceDeclaration())
                {
                    processNamespace(walker, namespaceDeclarationContext);
                }

                walker.PopScope();
            }
            else if (parserContext is CompilationUnitContext context2)
            {
                Name = "_global@" + SourceLocation.FileNameIdentifier;
                IsAnonymous = true;

                walker.PushScope(SyntaxScopeType.Namespace, this);

                foreach (var namespaceDeclarationContext in context2.namespaceDeclaration())
                {
                    processNamespace(walker, namespaceDeclarationContext);
                }
            }
            else throw new NotImplementedException();
        }

        private void processNamespace(SyntaxWalker walker, NamespaceDeclarationContext namespaceDeclarationContext)
        {
            InitialRoutines.AddRange(
                 namespaceDeclarationContext.children.OfType<InitialRoutineContext>()
                    .Select(x => Build<InitialRoutineDefinition>(walker, x)));

            Declarations.AddRange(
                 namespaceDeclarationContext.children.OfType<DeclarationContext>()
                    .Select(x => Build<Declaration>(walker, x)));

            SubNamespaces.AddRange(
                 namespaceDeclarationContext.children.OfType<NamespaceDefinitionContext>()
                    .Select(x => Build<NamespaceDefinition>(walker, x)));

            Functions.AddRange(
                 namespaceDeclarationContext.children.OfType<FunctionDefinitionContext>()
                    .Select(x => Build<FunctionDefinition>(walker, x)));

            Classes.AddRange(
                 namespaceDeclarationContext.children.OfType<ClassDefinitionContext>()
                    .Select(x => Build<ClassDefinition>(walker, x)));

            Enums.AddRange(
                 namespaceDeclarationContext.children.OfType<EnumDefinitionContext>()
                    .Select(x => Build<EnumDefinition>(walker, x)));

            Interfaces.AddRange(
                 namespaceDeclarationContext.children.OfType<InterfaceDefinitionContext>()
                    .Select(x => Build<InterfaceDefinition>(walker, x)));

            InterfaceImplementations.AddRange(
                 namespaceDeclarationContext.children.OfType<InterfaceForImplementationContext>()
                    .Select(x => Build<InterfaceForImplementation>(walker, x)));
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.namespaceDefinition(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public List<InitialRoutineDefinition> InitialRoutines { get; private set; } = [];

        [ChildrenNode]
        public List<Declaration> Declarations { get; private set; } = [];

        [ChildrenNode]
        public List<NamespaceDefinition> SubNamespaces { get; private set; } = [];

        [ChildrenNode]
        public List<FunctionDefinition> Functions { get; private set; } = [];

        [ChildrenNode]
        public List<ClassDefinition> Classes { get; private set; } = [];

        [ChildrenNode]
        public List<EnumDefinition> Enums { get; private set; } = [];

        [ChildrenNode]
        public List<InterfaceDefinition> Interfaces { get; private set; } = [];

        [ChildrenNode]
        public List<InterfaceForImplementation> InterfaceImplementations { get; private set; } = [];

        public bool IsEmpty => InitialRoutines.Count == 0 && Declarations.Count == 0 && Functions.Count == 0 && Classes.Count == 0 && Enums.Count == 0 && Interfaces.Count == 0 && InterfaceImplementations.Count == 0;

        public string Name { get; private set; } = "";

        public SyntaxScopeType ScopeType => SyntaxScopeType.Namespace;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous { get; private set; }

        public string Fullname
        {
            get
            {
                string result = Name;
                var current = this as ISyntaxScope;
                while (current.ParentScope is not null)
                {
                    result = current.ParentScope.Name + "." + result;
                    current = current.ParentScope;
                }
                return result;
            }
        }
    }
}