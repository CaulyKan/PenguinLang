using BabyPenguin.SemanticInterface;

namespace BabyPenguin.VirtualMachine
{
    public static class IRTypeClassifier
    {
        /// <summary>
        /// Maps a BabyPenguin IType to an IR type string matching EmperorPenguin's format.
        /// Key difference: string → "ref&lt;string&gt;" (reference type, not value type).
        /// </summary>
        public static string ToIrType(IType type)
        {
            var typeNode = type.TypeNode ?? throw new InvalidOperationException($"Type {type.FullName()} has no TypeNode");

            if (typeNode.IsBoolType) return "bool";
            if (typeNode.IsVoidType) return "void";
            if (typeNode.Type == TypeEnum.Char) return "char";

            if (typeNode.IsStringType) return "ref<string>";
            if (typeNode.Type == TypeEnum.U8) return "u8";
            if (typeNode.Type == TypeEnum.U16) return "u16";
            if (typeNode.Type == TypeEnum.U32) return "u32";
            if (typeNode.Type == TypeEnum.U64) return "u64";
            if (typeNode.Type == TypeEnum.I8) return "i8";
            if (typeNode.Type == TypeEnum.I16) return "i16";
            if (typeNode.Type == TypeEnum.I32) return "i32";
            if (typeNode.Type == TypeEnum.I64) return "i64";
            if (typeNode.Type == TypeEnum.Float) return "f32";
            if (typeNode.Type == TypeEnum.Double) return "f64";

            if (typeNode.IsEnumType)
            {
                return $"enum<{typeNode.FullName()}>";
            }

            if (typeNode.IsClassType)
            {
                if (IsValueClass(typeNode))
                    return $"struct<{typeNode.FullName()}>";
                return $"ref<{typeNode.FullName()}>";
            }

            if (typeNode.IsInterfaceType)
            {
                return $"ref<{typeNode.FullName()}>";
            }

            if (typeNode.IsFunctionType)
            {
                return "funptr";
            }

            return typeNode.FullName();
        }

        /// <summary>
        /// Returns true if the IR type string represents a value type.
        /// Value types: primitives (bool, i8-i64, u8-u64, f32, f64, char, void), enum&lt;...&gt;, struct&lt;...&gt;
        /// Reference types: ref&lt;...&gt;, funptr
        /// </summary>
        public static bool IsValueTypeIrType(string irType)
        {
            if (irType == "bool" || irType == "void" || irType == "char") return true;
            if (irType == "funptr") return false;
            if (irType.StartsWith("ref<")) return false;
            if (irType.StartsWith("i") || irType.StartsWith("u")) return true;
            if (irType.StartsWith("f")) return true;
            if (irType.StartsWith("enum<")) return true;
            if (irType.StartsWith("struct<")) return true;
            return false;
        }

        /// <summary>
        /// Returns true if the IR type string represents a reference type.
        /// </summary>
        public static bool IsReferenceTypeIrType(string irType)
        {
            return !IsValueTypeIrType(irType);
        }

        /// <summary>
        /// Checks if a class implements ICopy&lt;Self&gt;, making it a value type.
        /// This matches EmperorPenguin's is_value_class check.
        /// </summary>
        public static bool IsValueClass(ITypeNode typeNode)
        {
            if (typeNode is not IClassNode classNode) return false;

            var selfTypeName = typeNode.FullName();
            var expectedICopyName = $"__builtin.ICopy<{selfTypeName}>";

            foreach (var intf in classNode.ImplementedInterfaces)
            {
                if (intf.FullName() == expectedICopyName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the simple type name used in NEW instructions (e.g., "Node", "Pair").
        /// </summary>
        public static string GetTypeName(IType type)
        {
            return type.TypeNode?.FullName() ?? type.FullName();
        }
    }
}
