using BabyPenguin.SemanticInterface;

namespace BabyPenguin.Symbol
{
    public class EventSymbol : VariableSymbol
    {
        public EventSymbol(ISymbolContainer parent,
            bool isLocal,
            string name,
            IType type,
            IType eventType,
            SourceLocation sourceLocation,
            uint scopeDepth,
            string originName,
            bool isTemp,
            int? paramIndex,
            bool isClassMember) : base(parent, isLocal, name, type, sourceLocation, scopeDepth, originName, isTemp, paramIndex, isClassMember)
        {
            EventType = eventType;
        }

        public IType EventType { get; set; }
    }
}