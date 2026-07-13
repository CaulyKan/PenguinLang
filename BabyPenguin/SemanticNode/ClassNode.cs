namespace BabyPenguin.SemanticNode
{
    public interface IClassNode : ISemanticNode, ISemanticScope, ITypeNode, IRoutineContainer, ISymbolContainer, IVTableContainer
    {
        ITypeNode ITypeNode.Specialize(List<IType> genericArguments)
        {
            if (genericArguments.Count == 0)
                throw new BabyPenguinException("Cannot specialize without generic arguments.", null, code: ErrorCode.E_UNSUPPORTED);

            if (genericArguments.Count > 0 && genericArguments.Count != GenericDefinitions.Count)
                throw new BabyPenguinException("Count of generic arguments and definitions do not match.", null, code: ErrorCode.E_UNSUPPORTED);

            ClassNode result;
            if (SyntaxNode is ClassDefinition syntax)
            {
                result = new ClassNode(Model, syntax);
            }
            else
            {
                result = new ClassNode(Model, Name);
            }

            (result as IClassNode).GenericType = this;
            GenericInstances.Add(result);
            result.GenericArguments = genericArguments;
            result.Parent = Parent;
            Model.CatchUp(result);

            return result;
        }

        IFunction? Constructor { get; set; }
    }

    public class ClassNode : BaseSemanticNode, IClassNode
    {
        public ClassNode(SemanticModel model, string name, List<string>? genericDefinitions = null) : base(model)
        {
            Name = name;
            GenericDefinitions = genericDefinitions ?? [];
        }

        public ClassNode(SemanticModel model, ClassDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            GenericDefinitions = syntaxNode.Template?.Parameters.Where(p => p.IsTypeParameter).Select(gd => gd.Name!.Name).ToList() ?? [];
        }

        public List<IFunction> Functions { get; } = [];

        public List<IInitialRoutine> InitialRoutines { get; } = [];

        public string Name { get; }

        private string? _fullName;
        public string FullName()
        {
            if (_fullName != null)
                return _fullName;

            var n = Namespace == null ? Name : $"{Namespace.Name}.{Name}";
            if (GenericDefinitions.Count > 0)
            {
                if (GenericArguments.Count > 0)
                {
                    n += "<" + string.Join(",", GenericArguments.Select(t => t.FullName())) + ">";
                }
                else
                {
                    n += "<" + string.Join(",", GenericDefinitions.Select(t => "?")) + ">";
                }
            }
            _fullName = n;
            return n;
        }

        public INamespace Namespace => Parent as Namespace ?? throw new BabyPenguinException("Class is not inserted into model yet.", null, code: ErrorCode.E_UNSUPPORTED);

        public List<ISymbol> Symbols { get; } = [];

        public ISemanticScope? Parent { get; set; }

        public IEnumerable<ISemanticScope> Children => Functions.Cast<ISemanticScope>().Concat(InitialRoutines).Concat(VTables).Concat(OnRoutines);

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<string> GenericDefinitions { get; }

        public TypeEnum Type => TypeEnum.Class;

        public ITypeNode? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<ITypeNode> GenericInstances { get; set; } = [];

        public IFunction? Constructor { get; set; }

        public override string ToString() => (this as ISemanticScope).FullName();

        public IType ToType(Mutability isMutable)
        {
            return new ClassType(this.Model, this, isMutable);
        }

        public List<VTable> VTables { get; } = [];

        public List<IOnRoutine> OnRoutines { get; } = [];
    }
}
