using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IInitialRoutine : ISemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer;

    public class InitialRoutine : BaseSemanticNode, IInitialRoutine
    {
        public InitialRoutine(SemanticModel model, string name) : base(model)
        {
            Name = name;
        }

        public InitialRoutine(SemanticModel model, PenguinLangSyntax.InitialRoutine syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
        }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public string Name { get; }

        public string FullName => Parent!.FullName + "." + Name;

        public List<VirtualMachine.BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => (SyntaxNode as PenguinLangSyntax.InitialRoutine)?.CodeBlock;

        public IType ReturnTypeInfo { get; set; } = BasicType.Void;

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public List<ISymbol> Symbols { get; } = [];

        public override string ToString() => (this as ISemanticScope).FullName;
    }
}