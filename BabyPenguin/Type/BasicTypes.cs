
using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.Type
{

    public class BasicType(SemanticModel model, BasicTypeNode type, Mutability isMutable) : IType
    {
        public BasicTypeNode BasicTypeNode { get; } = type;

        public ITypeNode TypeNode => BasicTypeNode;

        public SemanticModel Model { get; } = model;

        public string Name => TypeNode!.Name;

        public List<IType> GenericArguments => TypeNode!.GenericArguments;

        public INamespace? Namespace => null;

        public TypeEnum Type => BasicTypeNode.Type;

        public Mutability IsMutable { get; } = isMutable;

        public bool CanImplicitlyCastToWithoutMutability(IType other)
        {
            if (BasicTypeNode.FullName() == other.TypeNode?.FullName())
                return true;

            return other.TypeNode is BasicTypeNode n && implicitlyCastOrders.ContainsKey(Type)
                && implicitlyCastOrders[Type].Contains(n.Type);
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this.IsMutable == isMutable ? this : new BasicType(Model, BasicTypeNode, isMutable);
        }

        public override string ToString() => (this as IType).FullName();

        static readonly Dictionary<TypeEnum, List<TypeEnum>> implicitlyCastOrders = new Dictionary<TypeEnum, List<TypeEnum>>{
            {
                TypeEnum.I8,
                [
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I16,
                [
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I32,
                [
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I64,
                [
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U8,
                [
                    TypeEnum.U16,
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I8,
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U16,
                [
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U32,
                [
                    TypeEnum.U64,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U64,
                [
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Float,
                [
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Double,
                [
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Bool,
                [
                    TypeEnum.String
                ]
            },
        };
    }

    public class TypeReferenceType(IType literalType) : IType
    {
        public IType TypeReference { get; set; } = literalType;

        public INamespace? Namespace => throw new NotImplementedException();

        public TypeEnum Type => TypeEnum.TypeReference;

        public List<string> GenericDefinitions => [];

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances => [];

        public IType? GenericType { get; set; } = null;

        public string Name => TypeReference.Name;

        public string FullName() => TypeReference.FullName();

        public SemanticModel Model => throw new NotImplementedException();

        public SourceLocation SourceLocation => throw new NotImplementedException();

        public SyntaxNode? SyntaxNode => null;

        public int PassIndex { get; set; }

        public Mutability IsMutable => Mutability.Immutable;

        public ITypeNode TypeNode => throw new NotImplementedException();

        public bool CanImplicitlyCastToWithoutMutability(IType other) => false;

        public IType Specialize(List<IType> genericArguments)
        {
            throw new NotImplementedException();
        }

        public IType WithMutability(Mutability isMutable)
        {
            return this;
        }
    }
}