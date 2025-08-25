using System.Reflection;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BabyPenguin.Type
{

    public interface IType
    {
        ITypeNode TypeNode { get; }

        SemanticModel Model { get; }

        string Name { get; }

        string FullName()
        {
            string n;
            if (TypeNode != null)
            {
                n = TypeNode.FullName();
            }
            else n = Namespace == null ? Name : $"{Namespace.Name}.{Name}";

            if (this.IsMutable == Mutability.Mutable)
                n = "mut " + n;
            else if (this.IsMutable == Mutability.Immutable)
                n = "!mut " + n;

            return n;
        }

        List<IType> GenericArguments { get; }

        NameComponents NameComponents => NameComponents.ParseName(FullName());

        INamespace? Namespace { get; }

        TypeEnum Type => TypeNode.Type;

        bool CanImplicitlyCastToWithoutMutability(IType other);

        bool CanImplicitlyCastTo(IType other)
        {
            if (!this.CanImplicitlyCastToWithoutMutability(other))
                return false;

            if (this.IsMutable == other.IsMutable || this.IsMutable == Mutability.Mutable && other.IsMutable == Mutability.Immutable || this.IsSimpleValueType)
                return true;
            else
                return false;
        }

        static IType? ImplictlyCastResult(IType one, IType another)
        {
            if (one == another)
                return one;

            if (one.CanImplicitlyCastTo(another))
                return another;
            else if (another.CanImplicitlyCastTo(one))
                return one;
            else
                return null;
        }

        Mutability IsMutable { get; }

        IType WithMutability(Mutability isMutable);

        bool IsStringType => TypeNode.IsStringType;

        bool IsSignedIntType => TypeNode.IsSignedIntType;

        bool IsUnsignedIntType => TypeNode.IsUnsignedIntType;

        bool IsIntType => TypeNode.IsIntType;

        bool IsFloatType => TypeNode.IsFloatType;

        bool IsNumericType => TypeNode.IsNumericType;

        bool IsBoolType => TypeNode.IsBoolType;

        bool IsFunctionType => TypeNode.IsFunctionType;

        bool IsVoidType => TypeNode.IsVoidType;

        bool IsClassType => TypeNode.IsClassType;

        bool IsEnumType => TypeNode.IsEnumType;

        bool IsInterfaceType => TypeNode.IsInterfaceType;

        bool IsSimpleValueType => TypeNode.IsSimpleValueType;

        bool IsFutureType => TypeNode.IsFutureType;
    }
}