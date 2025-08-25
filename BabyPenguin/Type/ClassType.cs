
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

        public bool CanImplicitlyCastToWithoutMutability(IType other)
        {
            if (Class.FullName() == (other.TypeNode as IClassNode)?.FullName())
                return true;
            else if (other.TypeNode is IInterfaceNode intf)
                return Class.ImplementedInterfaces.Any(i => i.FullName() == intf.FullName());
            else
                return false;
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new ClassType(Model, Class, isMutable);
        }

        public override string ToString() => (this as IType).FullName();
    }
}