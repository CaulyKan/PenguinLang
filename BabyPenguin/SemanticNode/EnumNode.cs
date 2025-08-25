using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IEnumNode : ISemanticNode, ISemanticScope, ITypeNode, IRoutineContainer, ISymbolContainer, IVTableContainer
    {
        VariableSymbol? ValueSymbol { get; set; }

        List<EnumDeclaration> EnumDeclarations { get; set; }

        ITypeNode ITypeNode.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.");

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.");

            EnumNode result;
            if (SyntaxNode is EnumDefinition syntax)
            {
                result = new EnumNode(Model, syntax);
            }
            else
            {
                result = new EnumNode(Model, Name);
                result.EnumDeclarations = EnumDeclarations.Select(i => new EnumDeclaration(Model, result, i.Name, i.Value)).ToList();
            }

            result.GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }
    }

    public class EnumNode : BaseSemanticNode, IEnumNode
    {
        public EnumNode(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public EnumNode(SemanticModel model, EnumDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            GenericDefinitions = syntaxNode.GenericDefinitions?.TypeParameters.Select(gd => gd.Name).ToList() ?? [];
        }

        public string Name { get; }

        public INamespace Namespace => Parent as Namespace ?? throw new BabyPenguinException("Enum is not inserted into model yet.");

        public List<IInitialRoutine> InitialRoutines { get; } = [];

        public List<IFunction> Functions { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public VariableSymbol? ValueSymbol { get; set; }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines).Concat(VTables).Concat(OnRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<EnumDeclaration> EnumDeclarations { get; set; } = [];

        public List<string> GenericDefinitions { get; }

        public ITypeNode? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<ITypeNode> GenericInstances { get; set; } = [];

        public List<VTable> VTables { get; } = [];

        public TypeEnum Type => TypeEnum.Enum;

        public override string ToString() => (this as ISemanticScope).FullName();

        public IType ToType(Mutability isMutable)
        {
            return new EnumType(this.Model, this, isMutable);
        }

        public List<IOnRoutine> OnRoutines { get; } = [];
    }


    public class EnumDeclaration : BaseSemanticNode
    {
        public EnumDeclaration(SemanticModel model, IEnumNode enum_, string name, int value, IType? typeInfo = null) : base(model)
        {
            Name = name;
            EnumNode = enum_;
            Value = value;
            TypeInfo = typeInfo ?? model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        public EnumDeclaration(SemanticModel model, IEnumNode enum_, PenguinLangSyntax.SyntaxNodes.EnumDeclaration syntaxNode, int value) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            EnumNode = enum_;
            Value = value;
            TypeInfo = model.BasicTypeNodes.Void.ToType(Mutability.Immutable); // Type is elaborated in SymbolElaboratePass
        }

        public string Name { get; }

        public IEnumNode EnumNode { get; }

        public IType TypeInfo { get; set; }

        public EnumSymbol? MemberSymbol { get; set; }

        public int Value { get; set; }
    }
}