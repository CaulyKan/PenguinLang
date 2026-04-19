namespace BabyPenguin.Symbol
{

    public class VariableSymbol(ISymbolContainer parent,
        bool isLocal,
        string name,
        IType type,
        SourceLocation sourceLocation,
        string originName,
        bool isTemp,
        int? paramIndex,
        bool isClassMember,
        Declaration? declaration,
        uint declaringScopeId = 0) : ISymbol
    {
        public string FullName() => Parent.FullName() + "." + Name;

        public string Name { get; } = name;

        public ISymbolContainer Parent { get; } = parent;

        public IType TypeInfo { get; set; } = type;

        public SourceLocation SourceLocation { get; } = sourceLocation;

        public bool IsLocal { get; } = isLocal;

        public string OriginName { get; } = originName;

        /// <summary>
        /// Unique ID of the scope where this variable was declared.
        /// Used to distinguish sibling scopes at the same depth.
        /// </summary>
        public uint DeclaringScopeId { get; } = declaringScopeId;

        public bool IsTemp { get; } = isTemp;

        public bool IsParameter { get; } = paramIndex.HasValue && paramIndex >= 0;

        public int ParameterIndex { get; } = paramIndex ?? -1;

        public bool IsClassMember { get; } = isClassMember;

        public bool IsStatic { get; } = !isClassMember && !isLocal;

        public bool IsEnum => false;

        public bool IsFunction => false;

        public bool IsVariable => true;

        public TypeInferStatus TypeInferStatus { get; set; } = TypeInferStatus.ExplicitTyped;

        public Declaration? Declaration { get; } = declaration;

        public Mutability IsMutable { get; set; } = type.IsMutable;

        public override string ToString()
        {
            return $"{Name}({TypeInfo})";
        }
    }
}