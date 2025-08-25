
namespace BabyPenguin.Symbol
{
    public class MutableSymbolProxy : ISymbol
    {
        public ISymbol Symbol { get; set; }

        public string Name => Symbol.Name;

        public string OriginName => Symbol.Name;

        public uint ScopeDepth => Symbol.ScopeDepth;

        public ISymbolContainer Parent => Symbol.Parent;

        public IType TypeInfo => Symbol.TypeInfo;

        public SourceLocation SourceLocation => Symbol.SourceLocation;

        public bool IsLocal => Symbol.IsLocal;

        public bool IsTemp => Symbol.IsTemp;

        public bool IsParameter => Symbol.IsParameter;

        public int ParameterIndex => Symbol.ParameterIndex;

        public bool IsClassMember => Symbol.IsClassMember;

        public bool IsStatic => Symbol.IsStatic;

        public bool IsEnum => Symbol.IsEnum;

        public bool IsFunction => Symbol.IsFunction;

        public bool IsVariable => Symbol.IsVariable;

        public Mutability IsMutable { get; set; }

        public MutableSymbolProxy(ISymbol symbol, Mutability isMutable)
        {
            var s = symbol;
            while (s is MutableSymbolProxy proxy)
                s = proxy.Symbol;
            Symbol = s;
            IsMutable = isMutable;
        }

        public string FullName() => Symbol.FullName();

        public override string ToString()
        {
            return $"*{Symbol}";
        }
    }
}