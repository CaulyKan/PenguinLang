
namespace BabyPenguin.Type
{
    public class EnumType(SemanticModel model, IEnumNode enm, Mutability isMutable) : IType
    {
        public IEnumNode Enum { get; } = enm;

        public ITypeNode TypeNode => Enum;

        public SemanticModel Model { get; } = model;

        public string Name => Enum.Name;

        public List<IType> GenericArguments => Enum.GenericArguments;

        public INamespace? Namespace => Enum.Namespace;

        public TypeEnum Type => TypeEnum.Enum;

        public Mutability IsMutable { get; } = isMutable;

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
            if (other.TypeNode is IEnumNode enm && TypeStructure.SameNode(Enum, enm))
                return true;
            else if (other.TypeNode is IInterfaceNode intf)
                return ImplementedInterfaceMatches(intf);
            else
                return false;
        }

        // See ClassType.ImplementedInterfaceMatches: structural comparison of
        // built vtables with a declared-impl fallback for specializations whose
        // CatchUp has not reached the interface pass yet.
        private bool ImplementedInterfaceMatches(IInterfaceNode intf)
        {
            foreach (var implemented in Enum.ImplementedInterfaces)
            {
                if (TypeStructure.SameNode(implemented, intf))
                    return true;
            }
            if (Enum.SyntaxNode is EnumDefinition syntax)
            {
                foreach (var impl in syntax.InterfaceImplementations)
                {
                    if (impl.InterfaceType == null) continue;
                    var node = Model.ResolveTypeNode(impl.InterfaceType.Text, s => s is IInterfaceNode, Enum);
                    if (node != null && TypeStructure.SameNode(node, intf))
                        return true;
                }
            }
            return false;
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new EnumType(Model, Enum, isMutable);
        }

        public override string ToString() => (this as IType).FullName();
    }
}