using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public interface IEnum : ISemanticNode, ISemanticScope, IType, IRoutineContainer, ISymbolContainer
    {
        VaraibleSymbol? ValueSymbol { get; set; }

        List<EnumDeclaration> EnumDeclarations { get; set; }

        IType IType.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.");

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.");

            Enum result;
            if (SyntaxNode is EnumDefinition syntax)
            {
                result = new Enum(Model, syntax);
            }
            else
            {
                result = new Enum(Model, Name);
                result.EnumDeclarations = EnumDeclarations.Select(i => new EnumDeclaration(Model, result, i.Name, i.Value)).ToList();
            }

            (result as IType).GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }
    }

    public class Enum : BaseSemanticNode, IEnum
    {
        public Enum(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public Enum(SemanticModel model, EnumDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            GenericDefinitions = syntaxNode.GenericDefinitions?.TypeParameters.Select(gd => gd.Name).ToList() ?? [];
        }

        public string Name { get; }

        public INamespace Namespace => Parent as Namespace ?? throw new BabyPenguinException("Enum is not inserted into model yet.");

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public List<Function> Functions { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public VaraibleSymbol? ValueSymbol { get; set; }

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<EnumDeclaration> EnumDeclarations { get; set; } = [];

        public TypeEnum Type => TypeEnum.Enum;

        public List<string> GenericDefinitions { get; }

        public IType? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances { get; set; } = [];

        public override bool Equals(object? obj) => (this as IEnum).FullName == (obj as IEnum)?.FullName;

        public override int GetHashCode() => (this as IEnum).FullName.GetHashCode();

        public bool CanImplicitlyCastTo(IType other) => (this as ISemanticScope).FullName == other.FullName;

        public override string ToString() => (this as ISemanticScope).FullName;
    }


    public class EnumDeclaration : BaseSemanticNode
    {
        public EnumDeclaration(SemanticModel model, IEnum enum_, string name, int value, IType? typeInfo = null) : base(model)
        {
            Name = name;
            Enum = enum_;
            Value = value;
            TypeInfo = typeInfo ?? BasicType.Void;
        }

        public EnumDeclaration(SemanticModel model, IEnum enum_, PenguinLangSyntax.EnumDeclaration syntaxNode, int value) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Enum = enum_;
            Value = value;
            TypeInfo = BasicType.Void; // Type is elaborated in SymbolElaboratePass
        }

        public string Name { get; }

        public IEnum Enum { get; }

        public IType TypeInfo { get; set; }

        public EnumSymbol? MemberSymbol { get; set; }

        public int Value { get; set; }

    }
}