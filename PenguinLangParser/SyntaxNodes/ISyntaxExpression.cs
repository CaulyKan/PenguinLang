namespace PenguinLangParser.SyntaxNodes
{

    public interface ISyntaxExpression : ISyntaxNode
    {
        bool IsSimple { get; }

        ISyntaxExpression GetEffectiveExpression();

    }
}