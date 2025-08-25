namespace BabyPenguin.SemanticNode
{
    public interface IInterfaceNode : ISemanticNode, ISemanticScope, ITypeNode, IRoutineContainer, ISymbolContainer, IVTableContainer
    {
        ITypeNode ITypeNode.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.");

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.");

            InterfaceNode result;
            if (SyntaxNode is InterfaceDefinition syntax)
            {
                result = new InterfaceNode(Model, syntax);
            }
            else
            {
                result = new InterfaceNode(Model, Name);
            }

            (result as IInterfaceNode).GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }

        bool HasDeclartion { get; set; }

        IFunction? Constructor { get; set; }
    }

    public class InterfaceNode : BaseSemanticNode, IInterfaceNode
    {
        public InterfaceNode(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public InterfaceNode(SemanticModel model, InterfaceDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            GenericDefinitions = syntaxNode.GenericDefinitions?.TypeParameters.Select(gd => gd.Name).ToList() ?? [];
        }

        public List<IFunction> Functions { get; } = [];

        public List<IInitialRoutine> InitialRoutines { get; } = [];

        public string Name { get; }

        public INamespace Namespace => Parent as Namespace ?? throw new BabyPenguinException("Class is not inserted into model yet.");

        public List<ISymbol> Symbols { get; } = [];

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines).Concat(VTables);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<string> GenericDefinitions { get; }

        public ITypeNode? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<ITypeNode> GenericInstances { get; set; } = [];

        public IFunction? Constructor { get; set; }

        public List<VTable> VTables { get; } = [];

        public TypeEnum Type => TypeEnum.Interface;

        public override string ToString() => (this as ISemanticScope).FullName();

        public IType ToType(Mutability isMutable)
        {
            return new InterfaceType(this.Model, this, isMutable);
        }

        public bool HasDeclartion { get; set; } = false;

        public List<IOnRoutine> OnRoutines { get; } = [];

        public Mutability IsMutable => Mutability.Immutable;
    }
}