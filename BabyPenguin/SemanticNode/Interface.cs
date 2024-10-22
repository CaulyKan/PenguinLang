namespace BabyPenguin.SemanticNode
{
    public interface IInterface : ISemanticNode, ISemanticScope, IType, IRoutineContainer, ISymbolContainer
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

            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }
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

        public List<Function> Functions { get; } = [];

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public string Name { get; }

        public INamespace Namespace => Parent as Namespace ?? throw new BabyPenguinException("Class is not inserted into model yet.");

        public List<ISymbol> Symbols { get; } = [];

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public TypeEnum Type => TypeEnum.Interface;

        public List<string> GenericDefinitions { get; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances { get; set; } = [];

        public override bool Equals(object? obj) => (this as IInterface).FullName == (obj as IInterface)?.FullName;

        public override int GetHashCode() => (this as IInterface).FullName.GetHashCode();

        public bool CanImplicitlyCastTo(IType other) => (this as ISemanticScope).FullName == other.FullName;

        public IFunction? Constructor { get; set; }

        public override string ToString() => (this as ISemanticScope).FullName;
    }

    public class InterfaceImplementation : BaseSemanticNode, ISemanticNode, IRoutineContainer
    {
        public InterfaceImplementation(SemanticModel model, string name, Interface interfaceType) : base(model)
        {
            Name = name;
            InterfaceType = interfaceType;
        }

        public InterfaceImplementation(SemanticModel model, PenguinLangSyntax.InterfaceImplementation syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
        }

        public string Name { get; }

        public string FullName => "impl_for_" + Name;

        public IInterface? InterfaceType { get; set; }

        public List<Function> Functions { get; } = [];

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces { get; } = [];
    }
}