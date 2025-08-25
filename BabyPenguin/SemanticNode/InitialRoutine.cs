using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IInitialRoutine : ISemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer
    {
        public FunctionSymbol? FunctionSymbol { get; set; }
    }

    public class InitialRoutine : BaseSemanticNode, IInitialRoutine
    {
        public InitialRoutine(SemanticModel model, string name) : base(model)
        {
            Name = name;
            ReturnTypeInfo = Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public InitialRoutine(SemanticModel model, InitialRoutineDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            ReturnTypeInfo = Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public string Name { get; }

        public string FullName() => Parent!.FullName() + "." + Name;

        public List<BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => (SyntaxNode as InitialRoutineDefinition)?.CodeBlock;

        public IType ReturnTypeInfo { get; set; }

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public List<ISymbol> Symbols { get; } = [];

        public FunctionSymbol? FunctionSymbol { get; set; }

        public override string ToString() => (this as ISemanticScope).FullName();
    }
}