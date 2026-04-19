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
            string originName,
            bool isTemp,
            int? paramIndex,
            bool isClassMember,
            Declaration? declaration) : base(parent, isLocal, name, type, sourceLocation, originName, isTemp, paramIndex, isClassMember, declaration)
        {
            EventType = eventType;
        }

        public IType EventType { get; set; }
    }
}