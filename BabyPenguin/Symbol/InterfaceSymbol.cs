namespace BabyPenguin.Symbol
{

    public class InterfaceVariableSymbol : VaraibleSymbol
    {
        public InterfaceVariableSymbol(
            VTable vtable,
            ISymbolContainer parent,
            bool isLocal,
            string name,
            IType interfaceType,
            SourceLocation sourceLocation,
            uint scopeDepth,
            string originName,
            bool isTemp,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember) : base(parent, isLocal, name, interfaceType, sourceLocation, scopeDepth, originName, isTemp, paramIndex, isReadonly, isClassMember)
        {
            VTable = vtable;
        }

        public VTable VTable { get; }
    }

}