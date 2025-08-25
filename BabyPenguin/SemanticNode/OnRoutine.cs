using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IOnRoutine : ISemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer
    {
        public FunctionSymbol? FunctionSymbol { get; set; }

        public IType? EventType { get; set; }

        public ISymbol? EventReceiverSymbol { get; set; }
    }

    public class OnRoutine : BaseSemanticNode, IOnRoutine
    {
        public OnRoutine(SemanticModel model, string name) : base(model)
        {
            Name = name;
            ReturnTypeInfo = model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public OnRoutine(SemanticModel model, OnRoutineDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            ReturnTypeInfo = model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public string Name { get; }

        public string FullName() => Parent!.FullName() + "." + Name;

        public List<BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => (SyntaxNode as OnRoutineDefinition)?.CodeBlock;

        public IType ReturnTypeInfo { get; set; }

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public List<ISymbol> Symbols { get; } = [];

        public FunctionSymbol? FunctionSymbol { get; set; }

        public override string ToString() => (this as ISemanticScope).FullName();

        public IType? EventType { get; set; }

        public ISymbol? EventReceiverSymbol { get; set; }
    }
}