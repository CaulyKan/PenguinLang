namespace BabyPenguin.Symbol
{

    public class EnumSymbol(ISymbolContainer parent, string name, IType type, int value, SourceLocation sourceLocation) : ISymbol
    {
        public string FullName => Parent.FullName + "." + Name;

        public string Name { get; } = name;

        public string OriginName { get; } = name;

        public uint ScopeDepth { get; } = 0;

        public ISymbolContainer Parent { get; } = parent;

        public IType TypeInfo { get; } = type;

        public SourceLocation SourceLocation { get; } = sourceLocation;

        public bool IsLocal { get; } = false;

        public bool IsTemp { get; } = false;

        public bool IsParameter { get; } = false;

        public int ParameterIndex { get; } = 0;

        public bool IsReadonly { get; } = true;

        public bool IsClassMember { get; } = false;

        public List<IType> GenericArguments { get; } = [];

        public List<string> GenericDefinitions { get; } = [];

        public int Value { get; } = value;

        public bool IsEnum => true;
        public bool IsFunction => false;
        public bool IsVariable => false;
        public bool IsStatic => false;
    }

}