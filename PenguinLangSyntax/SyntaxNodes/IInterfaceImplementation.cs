namespace PenguinLangSyntax.SyntaxNodes
{

    public interface IInterfaceImplementation
    {
        TypeSpecifier? InterfaceType { get; set; }

        string Name => InterfaceType!.Name;

        SyntaxScopeType ScopeType { get; }

        List<SyntaxSymbol> Symbols { get; set; }

        Dictionary<string, ISyntaxScope> SubScopes { get; set; }

        ISyntaxScope? ParentScope { get; set; }

        List<FunctionDefinition> Functions { get; set; }

        WhereDefinition? WhereDefinition { get; set; }
    }
}