namespace BabyPenguin.SemanticInterface
{

    public interface ISymbolContainer : ISemanticScope
    {
        List<ISymbol> Symbols { get; }

        static ulong counter = 0;

        ISymbol AllocTempSymbol(IType type, SourceLocation sourceLocation)
        {
            var name = $"__temp_{counter++}";
            if (!type.IsFunctionType)
            {
                ISymbol temp = new VariableSymbol(this, true, name, type, sourceLocation, 0, name, true, null, false, false);
                Symbols.Add(temp);
                return temp;
            }
            else
            {
                ISymbol temp = new FunctionVariableSymbol(this, true, name, sourceLocation, type.GenericArguments[0], type.GenericArguments.Skip(1).ToList(), 0, name, true, null, false, false, (type as BasicType)!.IsAsyncFunction);
                Symbols.Add(temp);
                return temp;
            }
        }

        ISymbol AddVariableSymbol(string name,
            bool isLocal,
            Or<string, IType> type,
            SourceLocation sourceLocation,
            uint scopeDepth,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember)
        {
            var originName = name;
            if (isLocal)
            {
                if (Model.ResolveShortSymbol(name, scope: this, scopeDepth: scopeDepth) != null)
                {
                    int i = 0;
                    while (Model.ResolveShortSymbol($"{name}_{i}", scope: this, scopeDepth: scopeDepth) != null)
                    {
                        i++;
                    }
                    name = $"{name}_{i}";
                }
            }

            var typeinfo = type.IsLeft ? Model.ResolveType(type.Left!, scope: this) : type.Right;

            if (typeinfo == null)
            {
                throw new BabyPenguinException($"Cant resolve type '{type}' for '{Name}'", sourceLocation);
            }

            ISymbol? symbol;
            if (!typeinfo.IsFunctionType)
            {
                symbol = new VariableSymbol(this, isLocal, name, typeinfo, sourceLocation, scopeDepth, originName, false, paramIndex, isReadonly, isClassMember);
            }
            else
            {
                if (typeinfo.GenericArguments.Count == 0)
                    throw new BabyPenguinException($"Function type '{typeinfo.FullName}' must have at least one generic arguments as return type", sourceLocation);

                var basicType = typeinfo as BasicType;

                symbol = new FunctionVariableSymbol(this, isLocal, name, sourceLocation, typeinfo.GenericArguments[0], typeinfo.GenericArguments.Skip(1).ToList(), scopeDepth, originName, false, paramIndex, isReadonly, isClassMember, basicType!.IsAsyncFunction);
            }
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName && !s.IsEnum))
                {
                    throw new BabyPenguinException($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                }
            }
            Symbols.Add(symbol);
            return symbol;
        }

        ISymbol AddInitialRoutineSymbol(IInitialRoutine initialRoutine,
            SourceLocation sourceLocation,
            uint scopeDepth,
            bool isClassMember)
        {
            var name = initialRoutine.Name;
            var originName = name;
            var symbol = new FunctionSymbol(this, initialRoutine, false, name, sourceLocation, BasicType.Void, [], scopeDepth, originName, false, -1, true, isClassMember, false, false, null);
            if (Model.Symbols.Any(s => s.FullName == symbol.FullName && !s.IsEnum))
            {
                throw new BabyPenguinException($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
            }

            Symbols.Add(symbol);
            return symbol;
        }

        ISymbol AddFunctionSymbol(IFunction func,
            bool isLocal,
            IType returnType,
            List<FunctionParameter> parameters,
            SourceLocation sourceLocation,
            uint scopeDepth,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember,
            bool isStatic,
            bool? isAsync = null)
        {
            var name = func.Name;
            var originName = name;
            if (isLocal)
            {
                if (Model.ResolveShortSymbol(name, scope: this, scopeDepth: scopeDepth) != null)
                {
                    int i = 0;
                    while (Model.ResolveShortSymbol(name, scope: this, scopeDepth: scopeDepth) != null)
                    {
                        i++;
                    }
                    name = $"{name}_{i}";
                }
            }

            var symbol = new FunctionSymbol(this, func, isLocal, name, sourceLocation, returnType, parameters, scopeDepth, originName, false, paramIndex, isReadonly, isClassMember, isStatic, func.IsExtern, isAsync);
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName && !s.IsEnum))
                {
                    throw new BabyPenguinException($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                }
            }
            Symbols.Add(symbol);
            return symbol;
        }

        ISymbol AddEnumSymbol(IEnum enum_, string name, IType typeInfo, int value, SourceLocation sourceLocation)
        {
            var symbol = new EnumSymbol(enum_, name, typeInfo, value, sourceLocation) as ISymbol;
            if (Model.Symbols.Any(s => s.FullName == symbol.FullName && s.IsEnum))
            {
                throw new BabyPenguinException($"Enum Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
            }
            Symbols.Add(symbol);
            return symbol;
        }

        ISymbol AddTypeReferenceSymbol(string name, IType typeReference, bool isLocal, uint scopeDepth, SourceLocation sourceLocation)
        {
            var symbol = new TypeReferenceSymbol(this, isLocal, name, typeReference, sourceLocation, scopeDepth);
            if (Model.Symbols.Any(s => s.FullName == symbol.FullName))
            {
                throw new BabyPenguinException($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
            }
            Symbols.Add(symbol);
            return symbol;
        }
    }

}