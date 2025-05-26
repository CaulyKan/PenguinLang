namespace BabyPenguin.SemanticInterface
{
    public interface IVTableContainer : ISemanticScope, IType
    {
        List<VTable> VTables { get; }

        IEnumerable<IInterface> ImplementedInterfaces => VTables.Select(v => v.Interface);
    }

    public record VTableSlot(ISymbol InterfaceSymbol, ISymbol ImplementationSymbol);

    public class VTable : BaseSemanticNode, ISemanticNode, IRoutineContainer, ISymbolContainer
    {
        public VTable(SemanticModel model, IVTableContainer implementingClass, IInterface interfaceType) : base(model)
        {
            Name = "vtable-" + interfaceType.FullName.Replace(".", "-");
            Parent = implementingClass;
            Interface = interfaceType;
        }

        public VTable(SemanticModel model, IInterfaceImplementation syntaxNode, IVTableContainer implementingClass) : base(model, syntaxNode as SyntaxNode)
        {
            var type = Model.ResolveType(syntaxNode.InterfaceType!.Text, s => s.IsInterfaceType, implementingClass);
            if (type is not IInterface interfaceType)
                throw new BabyPenguinException($"Could not resolve interface type {syntaxNode.InterfaceType.Text} in class {implementingClass.Name}");
            Name = "vtable-" + interfaceType.FullName.Replace(".", "-");
            Parent = implementingClass;
            Interface = interfaceType;
        }

        public IInterface Interface { get; }

        public List<VTableSlot> Slots { get; } = [];

        public string Name { get; }

        public ISemanticScope? Parent { get; set; }

        public List<IInitialRoutine> InitialRoutines => throw new NotImplementedException();

        public List<IFunction> Functions { get; } = [];

        public IEnumerable<ISemanticScope> Children => Functions;

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public string FullName => Parent!.FullName + "." + Name;

        public bool IsMerged { get; set; } = false;

        public List<IOnRoutine> OnRoutines { get; } = [];
    }

}