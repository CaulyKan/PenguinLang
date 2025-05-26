namespace BabyPenguin.SemanticNode
{
    public interface IClass : ISemanticNode, ISemanticScope, IType, IRoutineContainer, ISymbolContainer, IVTableContainer
    {
        IType IType.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.");

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.");

            // var namecomponents = NameComponents.ParseName(FullName);
            // var newNameComponents = new NameComponents(namecomponents.Prefix, namecomponents.Name, genericArguments.Select(a => a.FullName).ToList());
            // if (Model.ResolveType(newNameComponents.ToString()) is IClass specialized)
            //     return specialized;

            Class result;
            if (SyntaxNode is ClassDefinition syntax)
            {
                result = new Class(Model, syntax);
            }
            else
            {
                result = new Class(Model, Name);
            }

            (result as IClass).GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }

        bool IType.CanImplicitlyCastTo(IType other)
        {
            if (FullName == other.FullName)
                return true;
            else if (other is IInterface intf)
                return ImplementedInterfaces.Any(i => i.FullName == intf.FullName);
            else
                return false;
        }

        IFunction? Constructor { get; set; }
    }

    public class Class : BaseSemanticNode, IClass
    {
        public Class(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public Class(SemanticModel model, ClassDefinition syntaxNode) : base(model, syntaxNode)
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

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines).Concat(VTables).Concat(OnRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public TypeEnum Type => TypeEnum.Class;

        public List<string> GenericDefinitions { get; }

        public IType? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances { get; set; } = [];

        public override bool Equals(object? obj) => (this as IClass).FullName == (obj as IClass)?.FullName;

        public override int GetHashCode() => (this as IClass).FullName.GetHashCode();

        public IFunction? Constructor { get; set; }

        public override string ToString() => (this as ISemanticScope).FullName;

        public List<VTable> VTables { get; } = [];

        public List<IOnRoutine> OnRoutines { get; } = [];

    }
}