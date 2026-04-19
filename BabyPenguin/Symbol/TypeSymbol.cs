using System.Reflection;

namespace BabyPenguin.Symbol
{

    public class TypeReferenceSymbol(ISymbolContainer parent,
        bool isLocal,
        string name,
        IType typeReference,
        SourceLocation sourceLocation) : ISymbol
    {
        public string FullName() => Parent.FullName() + "." + Name;

        public string Name { get; } = name;

        public ISymbolContainer Parent { get; } = parent;

        public IType TypeInfo => new TypeReferenceType(TypeReference);

        public IType TypeReference { get; } = typeReference;

        public Mutability IsMutable { get; set; } = Mutability.Immutable;

        public SourceLocation SourceLocation { get; } = sourceLocation;

        public bool IsLocal { get; } = isLocal;

        public string OriginName { get; } = name;

        public bool IsTemp { get; } = false;

        public bool IsParameter { get; } = false;

        public int ParameterIndex { get; } = -1;

        public bool IsConst => true;

        public bool IsClassMember { get; } = false;

        public bool IsStatic { get; } = true;

        public bool IsEnum => false;

        public bool IsFunction => false;

        public bool IsVariable => false;

        public TypeInferStatus TypeInferStatus => TypeInferStatus.ExplicitTyped;

        public override string ToString()
        {
            return $"{TypeInfo}";
        }
    }

}