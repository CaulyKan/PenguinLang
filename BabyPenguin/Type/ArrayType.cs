namespace BabyPenguin.Type
{
    /// <summary>
    /// Dense native array type node `T[]`. Reference type (GC object, fixed
    /// length after creation). One node per element type node, cached on the
    /// SemanticModel (GetArrayTypeNode). Not generic in the template sense:
    /// the element is always a concrete type node at resolution time, so
    /// generic substitution (T -> i32) happens naturally when the element
    /// name is resolved inside a specialized scope.
    /// </summary>
    public class ArrayTypeNode : ITypeNode
    {
        public ArrayTypeNode(SemanticModel model, ITypeNode elementNode)
        {
            Model = model;
            ElementNode = elementNode;
        }

        public SemanticModel Model { get; }

        public ITypeNode ElementNode { get; }

        public string Name => "array<" + ElementNode.Name + ">";

        private string? _fullName;
        public string FullName() => _fullName ??= "array<" + ElementNode.FullName() + ">";

        public INamespace? Namespace => null;

        public TypeEnum Type => TypeEnum.Array;

        public List<string> GenericDefinitions => [];

        public ITypeNode? GenericType { get; set; }

        private List<IType>? _genericArguments;

        /// <summary>Element type as the single generic argument (for for-in desugaring etc.).</summary>
        public List<IType> GenericArguments
        {
            get => _genericArguments ??= [ElementNode.ToType(Mutability.Auto)];
            set => _genericArguments = value;
        }

        public List<ITypeNode> GenericInstances => [];

        public SourceLocation SourceLocation => SourceLocation.Empty();

        public SyntaxNode? SyntaxNode => null;

        public int PassIndex { get; set; } = int.MaxValue;

        public ITypeNode Specialize(List<IType> genericArguments)
            => throw new BabyPenguinException("Cannot specialize an array type.", null, code: ErrorCode.E_GENERIC_ARITY);

        override public string ToString() => FullName();

        private IType? _immutable;
        private IType? _mutable;

        public IType ToType(Mutability isMutable)
        {
            // Auto (bare `array<T>`, no prefix) defaults MUTABLE — arrays are
            // reference objects and the whole magic-method surface takes mut
            // receivers; the old string path carried this via the folded
            // "mut " name prefix. Only an explicit Immutable stays immutable.
            if (isMutable == Mutability.Mutable || isMutable == Mutability.Auto)
                return _mutable ??= new ArrayType(Model, this, Mutability.Mutable);
            return _immutable ??= new ArrayType(Model, this, isMutable);
        }
    }

    /// <summary>
    /// The IType wrapper for `T[]`. Identity is per (element node, mutability):
    /// two array types are cast-compatible only when their element nodes are
    /// the same object (no covariance).
    /// </summary>
    public class ArrayType(SemanticModel model, ArrayTypeNode node, Mutability isMutable) : IType
    {
        public SemanticModel Model { get; } = model;

        public ArrayTypeNode ArrayTypeNode { get; } = node;

        public ITypeNode TypeNode => ArrayTypeNode;

        public string Name => ArrayTypeNode.Name;

        private string? _fullName;
        public string FullName()
        {
            if (_fullName != null)
                return _fullName;
            var n = ArrayTypeNode.FullName();
            if (IsMutable == Mutability.Mutable)
                n = "mut " + n;
            else if (IsMutable == Mutability.Immutable)
                n = "!mut " + n;
            _fullName = n;
            return n;
        }

        public List<IType> GenericArguments => ArrayTypeNode.GenericArguments;

        /// <summary>Element type with immutable default mutability (matches ResolveType conventions).</summary>
        public IType ElementType => ArrayTypeNode.ElementNode.ToType(Mutability.Immutable);

        public INamespace? Namespace => null;

        public TypeEnum Type => TypeEnum.Array;

        public Mutability IsMutable { get; } = isMutable;

        public bool CanImplicitlyCastToWithoutMutability(IType other)
            => other.Type == TypeEnum.Array && other.TypeNode == ArrayTypeNode;

        public IType WithMutability(Mutability isMutable)
        {
            if (isMutable == Mutability.Auto)
                isMutable = Mutability.Immutable;
            return IsMutable == isMutable ? this : ArrayTypeNode.ToType(isMutable) as IType ?? this;
        }

        public override string ToString() => (this as IType).FullName();
    }
}
