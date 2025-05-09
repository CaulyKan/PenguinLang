using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IFunction : ISemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer
    {
        List<FunctionParameter> Parameters { get; }

        bool IsExtern { get; set; }

        bool? IsStatic { get; set; }

        bool? IsPure { get; set; }

        bool IsDeclarationOnly { get; }

        bool? IsAsync { get; set; }

        bool? IsGenerator { get; set; }

        FunctionSymbol? FunctionSymbol { get; set; }
    }

    public class Function : BaseSemanticNode, IFunction
    {
        public Function(SemanticModel model, string name, List<FunctionParameter>? parameters = null, IType? returnType = null, SourceLocation? sourceLocation = null, bool isExtern = false, bool isStatic = false, bool isPure = false, bool isDeclarationOnly = false, bool returnValueIsReadonly = false, bool? isAsync = false, bool? isGenerator = false) : base(model)
        {
            Name = name;
            IsExtern = isExtern;
            IsStatic = isStatic;
            IsPure = isPure;
            IsAsync = isAsync;
            IsGenerator = isGenerator;
            IsDeclarationOnly = isDeclarationOnly;
            ReturnValueIsReadonly = returnValueIsReadonly;
            SourceLocation = sourceLocation ?? SourceLocation.Empty();
            if (parameters != null)
                Parameters = parameters;
            if (returnType != null)
                ReturnTypeInfo = returnType;
        }

        public Function(SemanticModel model, FunctionDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            IsExtern = syntaxNode.IsExtern;
            IsPure = syntaxNode.IsPure;
            IsAsync = syntaxNode.IsAsync;
            IsDeclarationOnly = syntaxNode.CodeBlock == null && !IsExtern;
            ReturnValueIsReadonly = syntaxNode.ReturnValueIsReadonly ?? false;
        }

        public string Name { get; }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public string FullName => Parent!.FullName + "." + Name;

        public List<FunctionParameter> Parameters { get; } = [];

        public bool ReturnValueIsReadonly { get; }

        public IType ReturnTypeInfo { get; set; } = BasicType.Void;

        public bool IsExtern { get; set; }

        public bool? IsStatic { get; set; }

        public bool? IsPure { get; set; }

        public bool IsDeclarationOnly { get; }

        public bool? IsAsync { get; set; }

        public bool? IsGenerator { get; set; }

        public FunctionSymbol? FunctionSymbol { get; set; }

        public List<BabyPenguinIR> Instructions { get; } = [];

        public SyntaxNode? CodeSyntaxNode => (SyntaxNode as FunctionDefinition)?.CodeBlock;

        public ICodeContainer.CodeContainerStorage CodeContainerData { get; } = new();

        public override string ToString() => (this as ISemanticScope).FullName;
    }
}