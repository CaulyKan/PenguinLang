namespace BabyPenguin.SemanticPass
{
    public class SymbolElaboratePass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            var symbolContainers = Model.FindAll(o => o is ISymbolContainer).ToList();
            foreach (var obj in symbolContainers)
                ElaborateTypeReference(obj);

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

            ElaborateTypeReference(obj);
            ElaborateGlobalSymbol(obj);
            ElaborateLocalSymbol(obj);

            obj.PassIndex = PassIndex;
        }

        public void ElaborateTypeReference(ISemanticNode obj)
        {
            if (obj is INamespace ns && ns.SyntaxNode is NamespaceDefinition syntaxNode)
            {
                foreach (var typeRefDecl in syntaxNode.TypeReferenceDeclarations)
                {
                    var name = typeRefDecl.Identifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have an identifier", typeRefDecl.SourceLocation);
                    var typeName = typeRefDecl.TypeSpecifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have a type specifier", typeRefDecl.SourceLocation);
                    var type = Model.ResolveType(typeName, scope: ns);
                    if (type == null)
                        throw new BabyPenguinException($"Cant resolve type '{typeName}'", typeRefDecl.SourceLocation);
                    ns.AddTypeReferenceSymbol(typeRefDecl.Identifier.Name, type, false, typeRefDecl.ScopeDepth, typeRefDecl.SourceLocation);
                }
            }
            else if (obj is ISymbolContainer symbolContainer)
            {
                symbolContainer.SyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is TypeReferenceDeclaration typeRefDecl)
                    {
                        var name = typeRefDecl.Identifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have an identifier", typeRefDecl.SourceLocation);
                        var typeName = typeRefDecl.TypeSpecifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have a type specifier", typeRefDecl.SourceLocation);
                        var type = Model.ResolveType(typeName, scope: symbolContainer);
                        if (type == null)
                            throw new BabyPenguinException($"Cant resolve type '{typeName}'", typeRefDecl.SourceLocation);
                        symbolContainer.AddTypeReferenceSymbol(typeRefDecl.Identifier.Name, type, true, typeRefDecl.ScopeDepth, typeRefDecl.SourceLocation);
                    }
                    return true;
                });
            }
        }

        public void ElaborateGlobalSymbol(ISemanticNode obj)
        {
            void addEventSymbol(ISymbolContainer container, EventDefinition evt)
            {
                var typeName = evt.EventType?.TypeName ?? "void";
                var type = Model.ResolveType(typeName, scope: container)
                    ?? throw new BabyPenguinException($"Cant resolve type '{typeName}' for event '{evt.Name}'", evt.SourceLocation);
                var eventType = Model.ResolveType($"__builtin.Event<{type.FullName}>")
                    ?? throw new BabyPenguinException($"Can't resolve type __builtin.Event<{type.FullName}>");

                container.Symbols.Add(new EventSymbol(container, false, evt.Name, eventType, type, evt.SourceLocation, evt.ScopeDepth, evt.Name, false, null, false, false));
            }

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

                            foreach (var evt in syntaxNode.Events)
                            {
                                addEventSymbol(ns, evt);
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
                            foreach (var member in syntaxNode.Declarations)
                            {
                                cls.AddVariableSymbol(member.Name, false, member.TypeSpecifier!.Name, member.SourceLocation, member.ScopeDepth, null, member.IsReadonly, true);
                            }

                            foreach (var evt in syntaxNode.Events)
                            {
                                addEventSymbol(cls, evt);
                            }
                        }
                    }
                    break;
                case IInterface intf:
                    if (intf.IsGeneric && !intf.IsSpecialized)
                    {
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol elaboration for class '{intf.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        if (intf.SyntaxNode is InterfaceDefinition syntaxNode)
                        {
                            foreach (var member in syntaxNode.Declarations)
                            {
                                intf.HasDeclartion = true;
                                intf.AddVariableSymbol(member.Name, false, member.TypeSpecifier!.Name, member.SourceLocation, member.ScopeDepth, null, member.IsReadonly, true);
                            }

                            foreach (var evt in syntaxNode.Events)
                            {
                                intf.HasDeclartion = true;
                                addEventSymbol(intf, evt);
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
                        enm.ValueSymbol = enm.AddVariableSymbol("_value", false, BasicType.I32, enm.SourceLocation, 0, null, false, true) as VariableSymbol;

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

        public void ElaborateLocalSymbol(ISemanticNode obj)
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
                            if (item.Type == CodeBlockItem.CodeBlockItemType.Declaration)
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