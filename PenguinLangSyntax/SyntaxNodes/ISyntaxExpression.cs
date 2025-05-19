namespace PenguinLangSyntax.SyntaxNodes
{

    public interface ISyntaxExpression : ISyntaxNode
    {
        bool IsSimple { get; }

        ISyntaxExpression GetEffectiveExpression();

    }
}