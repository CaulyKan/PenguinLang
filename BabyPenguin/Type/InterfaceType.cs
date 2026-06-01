
namespace BabyPenguin.Type
{
    public class InterfaceType(SemanticModel model, IInterfaceNode intf, Mutability isMutable) : IType
    {
        public IInterfaceNode Interface { get; } = intf;

        public ITypeNode TypeNode { get; } = intf;

        public SemanticModel Model { get; } = model;

        public string Name => Interface.Name;

        public List<IType> GenericArguments => Interface.GenericArguments;

        public INamespace? Namespace => Interface.Namespace;

        public TypeEnum Type => TypeEnum.Interface;

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
            if (Interface.FullName() == (other.TypeNode as IInterfaceNode)?.FullName())
                return true;
            else if (other.TypeNode is IInterfaceNode intf)
                return Interface.ImplementedInterfaces.Any(i => i.FullName() == intf.FullName());
            else
                return false;
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new InterfaceType(Model, Interface, isMutable);
        }

        public override string ToString() => (this as IType).FullName();
    }
}