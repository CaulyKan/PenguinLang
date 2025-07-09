namespace BabyPenguin.SemanticNode
{
    public interface IInterface : ISemanticNode, ISemanticScope, IType, IRoutineContainer, ISymbolContainer, IVTableContainer
    {
        IType IType.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.");

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.");

            Interface result;
            if (SyntaxNode is InterfaceDefinition syntax)
            {
                result = new Interface(Model, syntax);
            }
            else
            {
                result = new Interface(Model, Name);
            }

            (result as IInterface).GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }

        bool IType.CanImplicitlyCastToWithoutMutability(IType other)
        {
            if (FullName() == other.WithMutability(false).FullName())
                return true;
            else if (other.WithMutability(false) is IInterface intf)
                return ImplementedInterfaces.Any(i => i.FullName() == intf.FullName());
            else
                return false;
        }

        bool HasDeclartion { get; set; }

        IFunction? Constructor { get; set; }
    }

    public class Interface : BaseSemanticNode, IInterface
    {
        public Interface(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public Interface(SemanticModel model, InterfaceDefinition syntaxNode) : base(model, syntaxNode)
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

        public TypeEnum Type => TypeEnum.Interface;

        public List<string> GenericDefinitions { get; }

        public IType? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances { get; set; } = [];

        public override bool Equals(object? obj) => (this as IInterface).FullName() == (obj as IInterface)?.FullName();

        public override int GetHashCode() => (this as IInterface).FullName().GetHashCode();

        public IFunction? Constructor { get; set; }

        public List<VTable> VTables { get; } = [];

        public override string ToString() => (this as ISemanticScope).FullName();

        public bool HasDeclartion { get; set; } = false;

        public List<IOnRoutine> OnRoutines { get; } = [];

        public bool IsMutable => false;
    }
}