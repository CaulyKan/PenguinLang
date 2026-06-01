
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
            if (Enum.FullName() == (other.TypeNode as IEnumNode)?.FullName())
                return true;
            else if (other.TypeNode is IInterfaceNode intf)
                return Enum.ImplementedInterfaces.Any(i => i.FullName() == intf.FullName());
            else
                return false;
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new EnumType(Model, Enum, isMutable);
        }

        public override string ToString() => (this as IType).FullName();
    }
}