namespace BabyPenguin.Symbol
{
    public interface ISymbol
    {
        string FullName { get; }

        string Name { get; }

        /// <summary>
        /// Original name of the symbol, before any renaming or aliasing.
        /// Symbol renaming may happen when a symbol is redecalred in a sub-scope.
        /// </summary>
        string OriginName { get; }

        /// <summary>
        /// Scope depth of the symbol, each '{}' block increases the depth by 1.
        /// </summary>
        uint ScopeDepth { get; }
        ISymbolContainer Parent { get; }
        IType TypeInfo { get; }
        SourceLocation SourceLocation { get; }
        bool IsLocal { get; }
        bool IsTemp { get; }
        bool IsParameter { get; }
        int ParameterIndex { get; }
        bool IsReadonly { get; }
        bool IsClassMember { get; }
        bool IsStatic { get; }
        bool IsEnum { get; }
        bool IsFunction { get; }
        bool IsVariable { get; }
    }
}