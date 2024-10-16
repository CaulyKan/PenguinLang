
using BabyPenguin;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface ISemanticNode
    {
        string Name { get; }

        string FullName { get; }

        SemanticModel Model { get; }

        SourceLocation SourceLocation { get; }

        SyntaxNode? SyntaxNode { get; }

        ErrorReporter Reporter => Model.Reporter;
    }

    public abstract class BaseSemanticNode
    {
        public SemanticModel Model { get; }

        public SourceLocation SourceLocation { get; }

        public SyntaxNode? SyntaxNode { get; }

        public BaseSemanticNode(SemanticModel model, SyntaxNode? syntaxNode = null)
        {
            Model = model;
            SourceLocation = syntaxNode?.SourceLocation ?? SourceLocation.Empty();
            SyntaxNode = syntaxNode;
        }
    }

}