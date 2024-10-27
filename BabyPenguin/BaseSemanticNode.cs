
namespace BabyPenguin
{
    public interface ISemanticNode
    {
        string Name { get; }

        string FullName { get; }

        SemanticModel Model { get; }

        SourceLocation SourceLocation { get; }

        SyntaxNode? SyntaxNode { get; }

        int PassIndex { get; set; }

        ErrorReporter Reporter => Model.Reporter;
    }

    public abstract class BaseSemanticNode
    {
        public SemanticModel Model { get; }

        public SourceLocation SourceLocation { get; }

        public SyntaxNode? SyntaxNode { get; }

        public int PassIndex { get; set; }

        public BaseSemanticNode(SemanticModel model, SyntaxNode? syntaxNode = null)
        {
            Model = model;
            SourceLocation = syntaxNode?.SourceLocation ?? SourceLocation.Empty();
            SyntaxNode = syntaxNode;
        }
    }

}