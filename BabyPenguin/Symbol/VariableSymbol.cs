namespace BabyPenguin.Symbol
{

    public class VariableSymbol(ISymbolContainer parent,
        bool isLocal,
        string name,
        IType type,
        SourceLocation sourceLocation,
        uint scopeDepth,
        string originName,
        bool isTemp,
        int? paramIndex,
        bool isClassMember) : ISymbol
    {
        public string FullName() => Parent.FullName() + "." + Name;

        public string Name { get; } = name;

        public ISymbolContainer Parent { get; } = parent;

        public IType TypeInfo { get; } = type;

        public SourceLocation SourceLocation { get; } = sourceLocation;

        public bool IsLocal { get; } = isLocal;

        public string OriginName { get; } = originName;

        public uint ScopeDepth { get; } = scopeDepth;

        public bool IsTemp { get; } = isTemp;

        public bool IsParameter { get; } = paramIndex.HasValue && paramIndex >= 0;

        public int ParameterIndex { get; } = paramIndex ?? -1;

        public bool IsClassMember { get; } = isClassMember;

        public bool IsStatic { get; } = !isClassMember && !isLocal;

        public bool IsEnum => false;

        public bool IsFunction => false;

        public bool IsVariable => true;

        public Mutability IsMutable { get; set; } = type.IsMutable;

        public override string ToString()
        {
            return $"{Name}({TypeInfo})";
        }
    }
}