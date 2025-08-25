namespace BabyPenguin.SemanticNode
{
    public interface INamespace : ISemanticNode, ISemanticScope, IRoutineContainer, ITypeContainer, ISymbolContainer
    {
        IFunction? Constructor { get; set; }
    }

    public class Namespace : BaseSemanticNode, INamespace
    {
        public Namespace(SemanticModel model, string name) : base(model)
        {
            Name = name;
            ReturnTypeInfo = Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public Namespace(SemanticModel model, NamespaceDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            ReturnTypeInfo = Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public List<ClassNode> Classes { get; } = [];

        public List<InterfaceNode> Interfaces { get; } = [];

        public List<EnumNode> Enums { get; } = [];

        public List<IFunction> Functions { get; } = [];

        public List<IInitialRoutine> InitialRoutines { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public List<IOnRoutine> OnRoutines { get; } = [];

        public string Name { get; }

        public string FullName() => Name;

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Classes.Cast<ISemanticScope>().Concat(Interfaces).Concat(Enums).Concat(Functions).Concat(InitialRoutines).Concat(OnRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => SyntaxNode;

        public IType ReturnTypeInfo { get; set; }

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public override string ToString() => (this as ISemanticScope).FullName();

        public IFunction? Constructor { get; set; }

    }

    public class MergedNamespace : ISemanticScope
    {
        public MergedNamespace(SemanticModel model, string name)
        {
            Name = name;
            Model = model;
        }

        public List<Namespace> Namespaces { get; } = [];

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Namespaces.Cast<ISemanticScope>();

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public string Name { get; }

        public string FullName() => Name;

        public SemanticModel Model { get; }

        public SourceLocation SourceLocation => SourceLocation.Empty();

        public SyntaxNode? SyntaxNode => null;

        public int PassIndex { get; set; }

        public IEnumerable<ClassNode> Classes => Namespaces.SelectMany(n => n.Classes);

        public IEnumerable<InterfaceNode> Interfaces => Namespaces.SelectMany(n => n.Interfaces);

        public IEnumerable<EnumNode> Enums => Namespaces.SelectMany(n => n.Enums);

        public IEnumerable<IFunction> Functions => Namespaces.SelectMany(n => n.Functions);

        public IEnumerable<IInitialRoutine> InitialRoutines => Namespaces.SelectMany(n => n.InitialRoutines);

        public IEnumerable<ISymbol> Symbols => Namespaces.SelectMany(n => n.Symbols);

        public IEnumerable<IOnRoutine> OnRoutines => Namespaces.SelectMany(n => n.OnRoutines);
    }
}