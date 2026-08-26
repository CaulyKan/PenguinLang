
namespace BabyPenguin.Type
{
    public class ClassType(SemanticModel model, IClassNode cls, Mutability isMutable) : IType
    {
        public IClassNode Class { get; } = cls;

        public Mutability IsMutable { get; } = isMutable;

        public ITypeNode TypeNode => Class;

        public SemanticModel Model { get; } = model;

        public string Name => Class.Name;

        public List<IType> GenericArguments => Class.GenericArguments;

        public INamespace? Namespace => Class.Namespace;

        public TypeEnum Type => TypeEnum.Class;

        private string? _fullName;
        public string FullName()
        {
            if (_fullName != null)
                return _fullName;
            string n = TypeNode.FullName();
            if (IsMutable == Mutability.Mutable)
                n = "mut " + n;
            else if (IsMutable == Mutability.Immutable)
                n = "!mut " + n;
            _fullName = n;
            return n;
        }

        public bool CanImplicitlyCastToWithoutMutability(IType other)
        {
            if (other.TypeNode is IClassNode cls && TypeStructure.SameNode(Class, cls))
                return true;
            else if (other.TypeNode is IInterfaceNode intf)
                return ImplementedInterfaceMatches(intf);
            else
                return false;
        }

        // Interface-implementation match for the implicit class->interface cast.
        // Two sources are consulted:
        //  1. the built VTables (the normal case — pass 05 has run for this node);
        //  2. the class's declared `impl` blocks, resolved in the class scope —
        //     bridges the window where a specialization was created mid-pass-04
        //     (a field default like `upstream : mut IChannel<T> = new _NeverSource<T>()`)
        //     and its CatchUp only ran passes up to 04, so no vtable exists yet.
        // Both compare structurally (mutability-insensitive on generic args):
        // `IChannel<!mut i64>` must satisfy `IChannel<i64>`-flavored targets.
        private bool ImplementedInterfaceMatches(IInterfaceNode intf)
        {
            foreach (var implemented in Class.ImplementedInterfaces)
            {
                if (TypeStructure.SameNode(implemented, intf))
                    return true;
            }
            if (Class.SyntaxNode is ClassDefinition syntax)
            {
                foreach (var impl in syntax.InterfaceImplementations)
                {
                    if (impl.InterfaceType == null) continue;
                    var node = Model.ResolveTypeNode(impl.InterfaceType.Text, s => s is IInterfaceNode, Class);
                    if (node != null && TypeStructure.SameNode(node, intf))
                        return true;
                }
            }
            return false;
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new ClassType(Model, Class, isMutable);
        }

        public override string ToString() => (this as IType).FullName();
    }
}