namespace BabyPenguin.SemanticPass
{
    public interface ISymbolContainer : ISemanticScope
    {
        List<ISymbol> Symbols { get; }

        static ulong counter = 0;

        ISymbol AllocTempSymbol(IType type, SourceLocation sourceLocation)
        {
            var name = $"__temp_{counter++}";
            ISymbol temp;
            temp = new VaraibleSymbol(this, true, name, type, sourceLocation, 0, name, true, null, false, false);
            Symbols.Add(temp);
            return temp;
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

            var symbol = new VaraibleSymbol(this, isLocal, name, typeinfo, sourceLocation, scopeDepth, originName, false, paramIndex, isReadonly, isClassMember);
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
    }

    public class SymbolElaboratePass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            var symbolContainers = Model.FindAll(o => o is ISymbolContainer).ToList();
            foreach (var obj in symbolContainers)
                ElaborateGlobalSymbol(obj);

            var codeContainers = Model.FindAll(o => o is ICodeContainer).ToList();
            foreach (var obj in codeContainers)
                ElaborateLocalSymbol(obj);

            foreach (var obj in symbolContainers)
                obj.PassIndex = PassIndex;

            foreach (var obj in codeContainers)
                obj.PassIndex = PassIndex;
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            ElaborateGlobalSymbol(obj);
            ElaborateLocalSymbol(obj);

            obj.PassIndex = PassIndex;
        }

        private void ElaborateGlobalSymbol(ISemanticNode obj)
        {
            switch (obj)
            {
                case INamespace ns:
                    {
                        if (ns.SyntaxNode is NamespaceDefinition syntaxNode)
                        {
                            foreach (var decl in syntaxNode.Declarations)
                            {
                                var typeName = decl.TypeSpecifier!.Name; // TODO: type inference
                                ns.AddVariableSymbol(decl.Name, false, typeName, decl.SourceLocation, decl.ScopeDepth, null, decl.IsReadonly, false);
                            }
                        }
                    }
                    break;
                case IClass cls:
                    if (cls.IsGeneric && !cls.IsSpecialized)
                    {
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol elaboration for class '{cls.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        if (cls.SyntaxNode is ClassDefinition syntaxNode)
                        {
                            foreach (var member in syntaxNode.ClassDeclarations)
                            {
                                cls.AddVariableSymbol(member.Name, false, member.TypeSpecifier!.Name, member.SourceLocation, member.ScopeDepth, null, member.IsReadonly, true);
                            }
                        }
                    }
                    break;
                case IEnum enm:
                    if (enm.IsGeneric && !enm.IsSpecialized)
                    {
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol elaboration for class '{enm.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        enm.ValueSymbol = enm.AddVariableSymbol("_value", false, BasicType.I32, enm.SourceLocation, 0, null, false, true) as VaraibleSymbol;

                        if (enm.SyntaxNode is EnumDefinition syntax)
                        {
                            enm.EnumDeclarations = syntax.EnumDeclarations.Select((e, i) => new SemanticNode.EnumDeclaration(Model, enm, e, i)).ToList();

                        }

                        for (int i = 0; i < enm.EnumDeclarations.Count; i++)
                        {
                            var enumDecl = enm.EnumDeclarations[i];
                            enumDecl.Value = i;

                            if (enumDecl.SyntaxNode is PenguinLangSyntax.SyntaxNodes.EnumDeclaration enumDeclSyntax)
                            {
                                if (enumDeclSyntax.TypeSpecifier != null)
                                {
                                    var type = Model.ResolveType(enumDeclSyntax.TypeSpecifier.Name, scope: enm);
                                    if (type == null)
                                        throw new BabyPenguinException($"Cant resolve type '{enumDeclSyntax.TypeSpecifier.Name}'", enumDeclSyntax.SourceLocation);
                                    enumDecl.TypeInfo = type;
                                }
                                else enumDecl.TypeInfo = BasicType.Void;
                            }

                            enumDecl.MemberSymbol = enm.AddEnumSymbol(enm, enumDecl.Name, enumDecl.TypeInfo, enumDecl.Value, enumDecl.SourceLocation) as EnumSymbol;
                        }
                    }
                    break;
                case IInitialRoutine initialRoutine:
                    {
                        var parent = initialRoutine.Parent as IType;
                        if (parent != null && parent.IsGeneric && !parent.IsSpecialized)
                        {
                            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol elaboration for initial routine '{initialRoutine.Name}' is skipped now because it is generic");
                        }
                        else
                        {
                            if (initialRoutine.SyntaxNode is InitialRoutineDefinition syntaxNode)
                            {
                                var funcSymbol = (initialRoutine.Parent as ISymbolContainer)!.AddInitialRoutineSymbol(
                                    initialRoutine, initialRoutine.SourceLocation, syntaxNode.ScopeDepth, false);

                                initialRoutine.FunctionSymbol = (FunctionSymbol)funcSymbol;
                            }
                        }
                    }
                    break;
                case IFunction func:
                    {
                        var parent = func.Parent as IType;
                        if (parent != null && parent.IsGeneric && !parent.IsSpecialized)
                        {
                            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol elaboration for function '{func.Name}' is skipped now because it is generic");
                        }
                        else
                        {
                            if (func.SyntaxNode is FunctionDefinition syntaxNode)
                            {
                                var retType = Model.ResolveType(syntaxNode.ReturnType!.Name, scope: func);
                                if (retType == null)
                                {
                                    throw new BabyPenguinException($"Cant resolve return type '{syntaxNode.ReturnType.Name}'", syntaxNode.SourceLocation);
                                }
                                else
                                {
                                    func.ReturnTypeInfo = retType;
                                }

                                func.Parameters.Clear();
                                int i = 0;
                                func.IsStatic = true;
                                foreach (var param in syntaxNode.Parameters)
                                {
                                    if (func.Parameters.Any(f => f.Name == param.Name))
                                    {
                                        throw new BabyPenguinException($"Duplicate parameter name '{param.Name}' for function '{syntaxNode.Name}'", param.SourceLocation);
                                    }
                                    else
                                    {
                                        var paramTypeName = param.TypeSpecifier!.Name; // TODO: type inference
                                        var paramType = Model.ResolveType(paramTypeName, scope: func);
                                        if (paramType == null)
                                        {
                                            throw new BabyPenguinException($"Cant resolve parameter type '{paramTypeName}' for param '{param.Name}'", param.SourceLocation);
                                        }
                                        else
                                        {
                                            func.Parameters.Add(new FunctionParameter(param.Name, paramType, param.IsReadonly, i));
                                            func.AddVariableSymbol(param.Name, true, new Or<string, IType>(paramType), param.SourceLocation, param.ScopeDepth, i, param.IsReadonly, false);
                                        }
                                    }

                                    if (param.Name == "this")
                                    {
                                        if ((func.Parent is IClass || func.Parent is IEnum || func.Parent is IInterface || func.Parent is VTable) && i == 0)
                                            func.IsStatic = false;
                                        else
                                            throw new BabyPenguinException($"'this' parameter can only be the first parameter for class method in function '{syntaxNode.Name}'", param.SourceLocation);
                                    }
                                    i++;
                                }

                                var funcSymbol = (func.Parent as ISymbolContainer)!.AddFunctionSymbol(func, false, func.ReturnTypeInfo, func.Parameters, syntaxNode.SourceLocation, syntaxNode.ScopeDepth, null, true, false, func.IsStatic!.Value);
                                func.FunctionSymbol = (FunctionSymbol)funcSymbol;
                            }
                            else
                            {
                                for (int i = 0; i < func.Parameters.Count; i++)
                                {
                                    var param = func.Parameters[i];
                                    func.AddVariableSymbol(param.Name, true, new Or<string, IType>(param.Type), func.SourceLocation, 0, i, param.IsReadonly, false);
                                }

                                func.FunctionSymbol = (func.Parent as ISymbolContainer)!.AddFunctionSymbol(func, false, func.ReturnTypeInfo, func.Parameters, func.SourceLocation, 0, null, true, false, func.IsStatic!.Value) as FunctionSymbol;
                            }
                        }
                        break;
                    }
                default:
                    break;
            }
        }

        private void ElaborateLocalSymbol(ISemanticNode obj)
        {
            if (obj is ICodeContainer container)
            {
                if (obj is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is IType t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Local Symbol elaborating pass for '{obj.FullName}' is skipped now because it is inside a generic type");
                }
                else
                {
                    container.CodeSyntaxNode?.TraverseChildren((node, _) =>
                    {
                        if (node is CodeBlockItem item)
                        {
                            if (item.IsDeclaration)
                            {
                                var typeName = item.Declaration!.TypeSpecifier!.Name; // TODO: type inference
                                container.AddVariableSymbol(item.Declaration.Name, true, typeName, item.SourceLocation, item.ScopeDepth, null, item.Declaration.IsReadonly, false);
                            }
                        }
                        else if (node is ForStatement forStatement)
                        {
                            var typeName = forStatement.Declaration!.TypeSpecifier!.Name;
                            container.AddVariableSymbol(forStatement.Declaration.Name, true, typeName, forStatement.Declaration.SourceLocation, forStatement.Declaration.ScopeDepth, null, forStatement.Declaration.IsReadonly, false);
                        }
                        return true;
                    });
                }
            }
        }

        public string Report
        {
            get
            {
                var table = new ConsoleTable("Name", "Type", "IsLocal", "Source");
                _ = Model.Symbols.Select(s => table.AddRow(s.FullName, s.TypeInfo, s.IsLocal, s.SourceLocation)).ToList();
                return table.ToMarkDownString();
            }
        }
    }
}