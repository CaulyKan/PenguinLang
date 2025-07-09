
namespace BabyPenguin.SemanticNode
{
    public class MutableTypeProxy(IType typeInfo) : IType
    {
        public IType TypeInfo { get; } = typeInfo;

        public INamespace? Namespace => TypeInfo.Namespace;

        public TypeEnum Type => TypeInfo.Type;

        public List<string> GenericDefinitions => TypeInfo.GenericDefinitions;

        public List<IType> GenericArguments { get => TypeInfo.GenericArguments; set => throw new NotImplementedException(); }

        public List<IType> GenericInstances => TypeInfo.GenericInstances;

        public IType? GenericType { get => TypeInfo.GenericType; set => throw new NotImplementedException(); }

        public bool IsMutable => true;

        public string Name => TypeInfo.Name;

        public SemanticModel Model => TypeInfo.Model;

        public SourceLocation SourceLocation => TypeInfo.SourceLocation;

        public SyntaxNode? SyntaxNode => TypeInfo.SyntaxNode;

        public int PassIndex { get => TypeInfo.PassIndex; set => throw new NotImplementedException(); }

        public bool CanImplicitlyCastToWithoutMutability(IType other)
        {
            return this.TypeInfo.CanImplicitlyCastToWithoutMutability(other);

        }

        public IType Specialize(List<IType> genericArguments)
        {
            throw new NotImplementedException();
        }

        public IType WithMutability(bool isReadonly)
        {
            if (isReadonly) return this;
            else return TypeInfo;
        }

        public override string ToString()
        {
            return (this as IType).FullName();
        }
    }
}