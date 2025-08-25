namespace BabyPenguin.SemanticInterface
{
    public interface ITypeNode : ISemanticNode
    {
        INamespace? Namespace { get; }

        string ISemanticNode.FullName()
        {
            var n = Namespace == null ? Name : $"{Namespace.Name}.{Name}";
            if (IsGeneric)
            {
                if (IsSpecialized)
                {
                    n += "<" + string.Join(",", GenericArguments.Select((t, i) => t.FullName())) + ">";
                }
                else
                {
                    n += "<" + string.Join(",", GenericDefinitions.Select(t => "?")) + ">";
                }
            }

            return n;
        }

        TypeEnum Type { get; }

        IType ToType(Mutability isMutable);

        List<string> GenericDefinitions { get; }

        List<IType> GenericArguments { get; set; }

        List<ITypeNode> GenericInstances { get; }

        bool IsGeneric => GenericDefinitions.Count > 0;

        bool IsSpecialized => !IsGeneric || GenericArguments.Count > 0;

        ITypeNode Specialize(List<IType> genericArguments);

        ITypeNode? GenericType { get; set; }

        ITypeNode? GetImplementedInterfaceType(Or<ITypeNode, string> interfaceTypeOrName, SourceLocation sourceLocation)
        {
            ITypeNode interfaceType = interfaceTypeOrName.IsLeft ? interfaceTypeOrName.Left! :
                (Model.ResolveTypeNode(interfaceTypeOrName.Right!) ?? throw new BabyPenguinException($"Could not resolve interface type '{interfaceTypeOrName.Right!}'", sourceLocation));

            if (this.FullName() == interfaceType.FullName())
                return this;

            if (this.GenericType?.FullName() == interfaceType.FullName())
                return this;

            if (this is IVTableContainer vtableContainer)
            {
                foreach (var vtable in vtableContainer.VTables)
                {
                    if (vtable.Interface.FullName() == interfaceType.FullName())
                        return vtable.Interface;
                    else if (vtable.Interface.GenericType?.FullName() == interfaceType.FullName())
                        return vtable.Interface;
                }
            }

            return null;
        }

        bool IsStringType => Type == TypeEnum.String;

        bool IsSignedIntType => Type == TypeEnum.I8 || Type == TypeEnum.I16 || Type == TypeEnum.I32 || Type == TypeEnum.I64;

        bool IsUnsignedIntType => Type == TypeEnum.U8 || Type == TypeEnum.U16 || Type == TypeEnum.U32 || Type == TypeEnum.U64;

        bool IsIntType => IsSignedIntType || IsUnsignedIntType;

        bool IsFloatType => Type == TypeEnum.Float || Type == TypeEnum.Double;

        bool IsNumericType => IsIntType || IsFloatType;

        bool IsBoolType => Type == TypeEnum.Bool;

        bool IsFunctionType => Type == TypeEnum.Fun;

        bool IsVoidType => Type == TypeEnum.Void;

        bool IsClassType => Type == TypeEnum.Class;

        bool IsEnumType => Type == TypeEnum.Enum;

        bool IsInterfaceType => Type == TypeEnum.Interface;

        bool IsComplexType => IsClassType || IsInterfaceType || IsEnumType;

        bool IsSimpleValueType => IsIntType || IsFloatType || IsBoolType || IsStringType || IsVoidType;

        bool IsFutureType
        {
            get
            {
                if (this.GenericType?.FullName() == "__builtin.IFuture<?>")
                    return true;

                if (this is IVTableContainer vtableContainer)
                {
                    foreach (var vtable in vtableContainer.VTables)
                        if (vtable.Interface.GenericType?.FullName() == "__builtin.IFuture<?>")
                            return true;
                }

                return false;
            }
        }
    }
}