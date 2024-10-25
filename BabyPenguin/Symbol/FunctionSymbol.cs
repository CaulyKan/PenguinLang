namespace BabyPenguin.Symbol
{

    public record FunctionParameter(string Name, IType Type, bool IsReadonly, int Index);

    public class FunctionSymbol : ISymbol
    {
        public FunctionSymbol(ISymbolContainer parent,
            IFunction func,
            bool isLocal,
            string name,
            SourceLocation sourceLocation,
            IType returnType,
            List<FunctionParameter> parameters,
            uint scopeDepth,
            string originName,
            bool isTemp,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember,
            bool isStatic)
        {
            Parent = parent;
            Name = name;
            ReturnTypeInfo = returnType;
            SourceLocation = sourceLocation;
            Parameters = parameters;
            IsLocal = isLocal;
            ScopeDepth = scopeDepth;
            OriginName = originName;
            IsParameter = paramIndex.HasValue && paramIndex >= 0;
            ParameterIndex = paramIndex ?? -1;
            IsTemp = isTemp;
            IsReadonly = isReadonly;
            SemanticFunction = func;
            IsStatic = isStatic;
            IsClassMember = isClassMember;

            var funTypeGenericArguments = new[] { returnType }.Concat(parameters.Select(p => p.Type)).ToList();
            var typeInfo = BasicType.Fun.Specialize(funTypeGenericArguments);
            if (typeInfo == null)
                throw new NotImplementedException();
            TypeInfo = typeInfo;
        }

        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; }
        public ISymbolContainer Parent { get; }
        public IType ReturnTypeInfo { get; }
        public List<FunctionParameter> Parameters { get; }
        public SourceLocation SourceLocation { get; }
        public IType TypeInfo { get; }
        public bool IsLocal { get; }
        public string OriginName { get; }
        public uint ScopeDepth { get; }
        public bool IsTemp { get; }
        public bool IsParameter { get; }
        public int ParameterIndex { get; }
        public bool IsReadonly { get; set; }
        public IFunction SemanticFunction { get; }
        public bool IsClassMember { get; }
        public bool IsStatic { get; }
        public List<IType> GenericArguments { get; } = [];
        public List<string> GenericDefinitions { get; } = [];

        public bool IsEnum => false;
        public bool IsFunction => true;
        public bool IsVariable => false;

        public override string ToString()
        {
            return $"{Name}({TypeInfo})";
        }
    }

}