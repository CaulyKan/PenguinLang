namespace BabyPenguin.SemanticNode
{
    public class BasicTypeNodes(SemanticModel model)
    {

        public BasicTypeNode Bool { get; } = new BasicTypeNode(model, "bool", TypeEnum.Bool);
        public BasicTypeNode Double { get; } = new BasicTypeNode(model, "double", TypeEnum.Double);
        public BasicTypeNode Float { get; } = new BasicTypeNode(model, "float", TypeEnum.Float);
        public BasicTypeNode String { get; } = new BasicTypeNode(model, "string", TypeEnum.String);
        public BasicTypeNode Void { get; } = new BasicTypeNode(model, "void", TypeEnum.Void);
        public BasicTypeNode U8 { get; } = new BasicTypeNode(model, "u8", TypeEnum.U8);
        public BasicTypeNode U16 { get; } = new BasicTypeNode(model, "u16", TypeEnum.U16);
        public BasicTypeNode U32 { get; } = new BasicTypeNode(model, "u32", TypeEnum.U32);
        public BasicTypeNode U64 { get; } = new BasicTypeNode(model, "u64", TypeEnum.U64);
        public BasicTypeNode I8 { get; } = new BasicTypeNode(model, "i8", TypeEnum.I8);
        public BasicTypeNode I16 { get; } = new BasicTypeNode(model, "i16", TypeEnum.I16);
        public BasicTypeNode I32 { get; } = new BasicTypeNode(model, "i32", TypeEnum.I32);
        public BasicTypeNode I64 { get; } = new BasicTypeNode(model, "i64", TypeEnum.I64);
        public BasicTypeNode Char { get; } = new BasicTypeNode(model, "char", TypeEnum.Char);
        public BasicTypeNode Fun { get; } = new BasicTypeNode(model, "fun", TypeEnum.Fun);
        public BasicTypeNode AsyncFun { get; } = new BasicTypeNode(model, "async_fun", TypeEnum.Fun) { IsAsyncFunction = true };

        public Dictionary<string, BasicTypeNode> Nodes => new() {
            { "bool", Bool },
            { "double", Double },
            { "float", Float },
            { "string", String },
            { "void", Void },
            { "u8", U8 },
            { "u16", U16 },
            { "u32", U32 },
            { "u64", U64 },
            { "i8", I8 },
            { "i16", I16 },
            { "i32", I32 },
            { "i64", I64 },
            { "char", Char },
        };

        public BasicTypeNode this[string i] => Nodes[i];

        public IType? ResolveLiteralType(string literal)
        {
            if (literal.StartsWith('"') && literal.EndsWith('"'))
            {
                return String.WithMutability(Mutability.Immutable);
            }
            else if (byte.TryParse(literal, out var _))
            {
                return U8.WithMutability(Mutability.Immutable);
            }
            else if (sbyte.TryParse(literal, out var _))
            {
                return I8.WithMutability(Mutability.Immutable);
            }
            else if (ushort.TryParse(literal, out var _))
            {
                return U16.WithMutability(Mutability.Immutable);
            }
            else if (short.TryParse(literal, out var _))
            {
                return I16.WithMutability(Mutability.Immutable);
            }
            else if (uint.TryParse(literal, out var _))
            {
                return U32.WithMutability(Mutability.Immutable);
            }
            else if (int.TryParse(literal, out var _))
            {
                return I32.WithMutability(Mutability.Immutable);
            }
            else if (ulong.TryParse(literal, out var _))
            {
                return U64.WithMutability(Mutability.Immutable);
            }
            else if (long.TryParse(literal, out var _))
            {
                return I64.WithMutability(Mutability.Immutable);
            }
            else if (literal == "true" || literal == "false")
            {
                return Bool.WithMutability(Mutability.Immutable);
            }
            else if (literal.StartsWith('\'') && literal.EndsWith('\''))
            {
                return Char.WithMutability(Mutability.Immutable);
            }
            else if (float.TryParse(literal, out var _))
            {
                return Float.WithMutability(Mutability.Immutable);
            }
            else if (double.TryParse(literal, out var _))
            {
                return Double.WithMutability(Mutability.Immutable);
            }
            else
            {
                return null;
            }
        }

    }


    public class BasicTypeNode : ITypeNode, IVTableContainer
    {
        public BasicTypeNode(SemanticModel model, string name, TypeEnum type)
        {
            Name = name;
            Type = type;
            Model = model;
        }

        public string Name { get; }

        public string FullName()
        {
            var result = Name;
            if (GenericArguments.Count > 0)
            {
                result += "<" + string.Join(",", GenericArguments.Select((t, i) => t.FullName())) + ">";
            }
            return result;
        }

        public INamespace? Namespace => null;

        public TypeEnum Type { get; }

        public List<string> GenericDefinitions { get; } = [];

        public ITypeNode? GenericType { get; set; }

        public List<IType> GenericArguments { get; set; } = [];

        public List<ITypeNode> GenericInstances { get; set; } = [];

        public SemanticModel Model { get; }

        public SourceLocation SourceLocation => SourceLocation.Empty();

        public SyntaxNode? SyntaxNode => null;

        public int PassIndex { get; set; } = int.MaxValue; // BasicType do not involve in semantic passes.

        public List<VTable> VTables { get; } = [];

        public ISemanticScope? Parent { get => null; set => throw new NotImplementedException(); }

        public IEnumerable<ISemanticScope> Children => [];

        public List<NamespaceImport> ImportedNamespaces => [];

        IEnumerable<MergedNamespace> ISemanticScope.GetImportedNamespaces(bool includeBuiltin) => [];

        public bool IsAsyncFunction { get; set; } = false;

        public Mutability IsMutable => Mutability.Immutable;

        public ITypeNode TypeNode => throw new NotImplementedException();

        public ITypeNode Specialize(List<IType> genericArguments)
        {
            if (this.Type == TypeEnum.Fun)
            {
                if (this.GenericArguments.Count > 0) throw new BabyPenguinException("Cannot specialize a specialized type.");

                var typeInfo = new BasicTypeNode(Model, "fun", TypeEnum.Fun)
                {
                    GenericArguments = genericArguments,
                    IsAsyncFunction = this.IsAsyncFunction
                };
                if (this.GenericInstances.Find(i => i.FullName() == typeInfo.FullName()) is ITypeNode existingType)
                    return existingType;
                this.GenericInstances.Add(typeInfo);
                typeInfo.GenericType = this;
                return typeInfo;
            }
            else
            {
                throw new BabyPenguinException("Cannot specialize a basic type.");
            }
        }

        override public string ToString() => FullName();

        public IType WithMutability(Mutability isMutable)
        {
            return new BasicType(Model, this, isMutable);
        }

        public IType ToType(Mutability isMutable)
        {
            return new BasicType(Model, this, isMutable);
        }
    }

}