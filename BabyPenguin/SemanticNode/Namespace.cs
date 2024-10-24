namespace BabyPenguin.SemanticNode
{
    public interface INamespace : ISemanticNode, ISemanticScope, IRoutineContainer, ITypeContainer, ISymbolContainer, ICodeContainer;

    public class Namespace : BaseSemanticNode, INamespace
    {
        public Namespace(SemanticModel model, string name) : base(model)
        {
            Name = name;
        }

        public Namespace(SemanticModel model, NamespaceDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
        }

        public List<Class> Classes { get; } = [];

        public List<Interface> Interfaces { get; } = [];

        public List<Enum> Enums { get; } = [];

        public List<Function> Functions { get; } = [];

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public string Name { get; }

        public List<ISymbol> Symbols { get; } = [];

        public string FullName => Name;

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Classes.Cast<ISemanticScope>().Concat(Interfaces).Concat(Enums).Concat(Functions).Concat(InitialRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => SyntaxNode;

        public IType ReturnTypeInfo { get; set; } = BasicType.Void;

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public override string ToString() => (this as ISemanticScope).FullName;
    }
}