using System.Reflection;

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
                    var name = typeRefDecl.Identifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have an identifier", typeRefDecl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                    var typeName = typeRefDecl.TypeSpecifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have a type specifier", typeRefDecl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                    var type = Model.ResolveType(typeName, scope: ns);
                    if (type == null)
                        throw new BabyPenguinException($"Cant resolve type '{typeName}'", typeRefDecl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                    ns.AddTypeReferenceSymbol(typeRefDecl.Identifier.Name, type, false, typeRefDecl.SourceLocation);
                }
            }
            else if (obj is ISymbolContainer symbolContainer)
            {
                symbolContainer.SyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is TypeReferenceDeclaration typeRefDecl)
                    {
                        var name = typeRefDecl.Identifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have an identifier", typeRefDecl.SourceLocation, code: ErrorCode.E_INTERNAL);
                        var typeName = typeRefDecl.TypeSpecifier?.Name ?? throw new BabyPenguinException($"Type reference declaration must have a type specifier", typeRefDecl.SourceLocation, code: ErrorCode.E_INTERNAL);
                        var type = Model.ResolveType(typeName, scope: symbolContainer);
                        if (type == null)
                            throw new BabyPenguinException($"Cant resolve type '{typeName}'", typeRefDecl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                        symbolContainer.AddTypeReferenceSymbol(typeRefDecl.Identifier.Name, type, true, typeRefDecl.SourceLocation);
                    }
                    return true;
                });
            }
        }

        public void ElaborateGlobalSymbol(ISemanticNode obj)
        {
            switch (obj)
            {
                case INamespace ns:
                    {
                        if (ns.SyntaxNode is NamespaceDefinition syntaxNode)
                        {
                            foreach (var decl in syntaxNode.Declarations)
                            {
                                var typeName = decl.TypeSpecifier?.Name ?? "";
                                ns.AddVariableSymbol(decl.Name, false, typeName, decl.SourceLocation, null, false, decl);
                            }
                        }
                    }
                    break;
                case IClassNode cls:
                    if (cls.IsGeneric && !cls.IsSpecialized)
                    {
                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Symbol elaboration for class '{cls.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        if (cls.SyntaxNode is ClassDefinition syntaxNode)
                        {
                            foreach (var member in syntaxNode.Declarations)
                            {
                                var typeName = member.TypeSpecifier?.Name ?? "";
                                cls.AddVariableSymbol(member.Name, false, typeName, member.SourceLocation, null, true, member, member.IsMutable);
                            }
                        }
                    }
                    break;
                case IInterfaceNode intf:
                    if (intf.IsGeneric && !intf.IsSpecialized)
                    {
                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Symbol elaboration for class '{intf.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        if (intf.SyntaxNode is InterfaceDefinition syntaxNode)
                        {
                            foreach (var member in syntaxNode.Declarations)
                            {
                                intf.HasDeclartion = true;
                                var typeName = member.TypeSpecifier?.Name ?? "";
                                intf.AddVariableSymbol(member.Name, false, typeName, member.SourceLocation, null, true, member);
                            }
                        }
                    }
                    break;
                case IEnumNode enm:
                    if (enm.IsGeneric && !enm.IsSpecialized)
                    {
                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Symbol elaboration for class '{enm.Name}' is skipped now because it is generic");
                    }
                    else
                    {
                        enm.ValueSymbol = enm.AddVariableSymbol("_value", false, new(Model.BasicTypeNodes.I32.ToType(Mutability.Auto)), enm.SourceLocation, null, true, null) as VariableSymbol;

                        if (enm.SyntaxNode is EnumDefinition syntax)
                        {
                            enm.EnumDeclarations = syntax.EnumDeclarations.Select((e, i) => new SemanticNode.EnumDeclaration(Model, enm, e, i)).ToList();

                        }

                        for (int i = 0; i < enm.EnumDeclarations.Count; i++)
                        {
                            var enumDecl = enm.EnumDeclarations[i];
                            enumDecl.Value = i;

                            if (enumDecl.SyntaxNode is PenguinLangParser.SyntaxNodes.EnumDeclaration enumDeclSyntax)
                            {
                                if (enumDeclSyntax.TypeSpecifier != null)
                                {
                                    var type = Model.ResolveType(enumDeclSyntax.TypeSpecifier.Name, scope: enm, useImmutableAsDefault: false);
                                    if (type == null)
                                        throw new BabyPenguinException($"Cant resolve type '{enumDeclSyntax.TypeSpecifier.Name}'", enumDeclSyntax.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                                    enumDecl.TypeInfo = type;
                                }
                                else enumDecl.TypeInfo = Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
                            }

                            enumDecl.MemberSymbol = enm.AddEnumSymbol(enm, enumDecl.Name, enumDecl.TypeInfo, enumDecl.Value, enumDecl.SourceLocation) as EnumSymbol;
                        }
                    }
                    break;
                case IInitialRoutine initialRoutine:
                    {
                        var parent = initialRoutine.Parent as IRoutineContainer;
                        if (parent is ITypeNode parentType && parentType.IsGeneric && !parentType.IsSpecialized)
                        {
                            Model.Reporter.Write(DiagnosticLevel.Debug, $"Symbol elaboration for initial routine '{initialRoutine.Name}' is skipped now because it is generic");
                        }
                        else
                        {
                            if (initialRoutine.SyntaxNode is InitialRoutineDefinition syntaxNode)
                            {
                                var funcSymbol = (initialRoutine.Parent as ISymbolContainer)!.AddInitialRoutineSymbol(
                                    initialRoutine, initialRoutine.SourceLocation, false);

                                initialRoutine.FunctionSymbol = (FunctionSymbol)funcSymbol;
                            }
                        }
                    }
                    break;
                case IFunction func:
                    {
                        var parent = func.Parent as ITypeNode;
                        if (parent != null && parent.IsGeneric && !parent.IsSpecialized)
                        {
                            Model.Reporter.Write(DiagnosticLevel.Debug, $"Symbol elaboration for function '{func.Name}' is skipped now because it is generic");
                        }
                        else
                        {
                            if (func.SyntaxNode is FunctionDefinition syntaxNode)
                            {
                                var retType = Model.ResolveType(syntaxNode.ReturnType!.Name, scope: func);
                                if (retType == null)
                                {
                                    throw new BabyPenguinException($"Cant resolve return type '{syntaxNode.ReturnType.Name}'", syntaxNode.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                                }
                                else
                                {
                                    func.ReturnTypeInfo = retType.IsMutable == Mutability.Auto ? retType.WithMutability(Mutability.Immutable) : retType;
                                }

                                func.Parameters.Clear();
                                int i = 0;
                                func.IsStatic = true;
                                foreach (var param in syntaxNode.Parameters)
                                {
                                    if (func.Parameters.Any(f => f.Name == param.Name))
                                    {
                                        throw new BabyPenguinException($"Duplicate parameter name '{param.Name}' for function '{syntaxNode.Name}'", param.SourceLocation, code: ErrorCode.E_DUPLICATE_PARAM);
                                    }
                                    else
                                    {
                                        var paramTypeName = param.TypeSpecifier!.Name; // TODO: type inference
                                        var paramType = Model.ResolveType(paramTypeName, scope: func);
                                        if (paramType == null)
                                        {
                                            throw new BabyPenguinException($"Cant resolve parameter type '{paramTypeName}' for param '{param.Name}'", param.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                                        }
                                        else
                                        {
                                            func.Parameters.Add(new FunctionParameter(param.Name, paramType, i));
                                            func.AddVariableSymbol(param.Name, true, new Or<string, IType>(paramType), param.SourceLocation, i, false, param);
                                        }
                                    }

                                    if (param.Name == "this")
                                    {
                                        if ((func.Parent is IClassNode || func.Parent is IEnumNode || func.Parent is IInterfaceNode || func.Parent is VTable) && i == 0)
                                            func.IsStatic = false;
                                        else
                                            throw new BabyPenguinException($"'this' parameter can only be the first parameter for class method in function '{syntaxNode.Name}'", param.SourceLocation, code: ErrorCode.E_INTERNAL);
                                    }
                                    i++;
                                }

                                var funcSymbol = (func.Parent as ISymbolContainer)!.AddFunctionSymbol(func, false, func.ReturnTypeInfo, func.Parameters, syntaxNode.SourceLocation, null, true, false, func.IsStatic!.Value, Mutability.Immutable);
                                func.FunctionSymbol = (FunctionSymbol)funcSymbol;
                            }
                            else
                            {
                                for (int i = 0; i < func.Parameters.Count; i++)
                                {
                                    var param = func.Parameters[i];
                                    func.AddVariableSymbol(param.Name, true, new Or<string, IType>(param.Type), func.SourceLocation, i, false, null);
                                }

                                func.FunctionSymbol = (func.Parent as ISymbolContainer)!.AddFunctionSymbol(func, false, func.ReturnTypeInfo, func.Parameters, func.SourceLocation, null, true, false, func.IsStatic!.Value, Mutability.Immutable) as FunctionSymbol;
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
                if (obj is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is ITypeNode t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Local Symbol elaborating pass for '{obj.FullName()}' is skipped now because it is inside a generic type");
                }
                else
                {
                    // Top-level construct blocks are hidden __construct_* functions;
                    // their lets hoist into the enclosing namespace (same symbol
                    // space as ordinary top-level lets) so initial routines can
                    // see the modules/channels the elaboration wired up.
                    IFunction? constructFunc = obj as IFunction;
                    var isConstruct = constructFunc != null && constructFunc.Name.StartsWith(SemanticScopingPass.ConstructPrefix)
                        && constructFunc.Parent is INamespace;
                    // Class-level constructs hoist their lets into the class as
                    // instance fields (submodules, channel objects).
                    var isClassConstruct = constructFunc != null && constructFunc.Name.StartsWith(SemanticScopingPass.ClassConstructPrefix)
                        && constructFunc.Parent is ITypeNode;

                    container.CodeSyntaxNode?.TraverseChildren((node, _) =>
                    {
                        if (node is CodeBlockItem item)
                        {
                            if (item.Type == CodeBlockItem.CodeBlockItemType.Declaration)
                            {
                                var typeName = item.Declaration!.TypeSpecifier?.Name ?? "";
                                if (isConstruct)
                                {
                                    var ns = (INamespace)constructFunc!.Parent!;
                                    ns.AddVariableSymbol(item.Declaration.Name, false, typeName, item.SourceLocation, null, false, item.Declaration, declaringScopeId: 0);
                                }
                                // class-construct lets stay function locals: submodules
                                // are referenced bare inside the wiring block only
                                // (class members are invisible to bare identifiers)
                                else
                                {
                                    container.AddVariableSymbol(item.Declaration.Name, true, typeName, item.SourceLocation, null, false, item.Declaration, declaringScopeId: item.ScopeId);
                                }
                            }
                        }
                        else if (node is ForStatement forStatement)
                        {
                            // Untyped for-loop variables (let x in ...) have no TypeSpecifier;
                            // the type is inferred from the iterator element at codegen time.
                            var typeName = forStatement.Declaration!.TypeSpecifier?.Name ?? "";
                            container.AddVariableSymbol(forStatement.Declaration.Name, true, typeName, forStatement.Declaration.SourceLocation, null, false, forStatement.Declaration, declaringScopeId: forStatement.ScopeId);
                        }
                        else if (node is TryStatement tryStatement)
                        {
                            // Catch variable: always __builtin.RuntimeError; the runtime
                            // binds the error object when dispatching to the handler.
                            var catchDecl = tryStatement.CatchDeclaration!;
                            if (catchDecl.TypeSpecifier != null && catchDecl.TypeSpecifier.Name != "__builtin.RuntimeError")
                                throw new BabyPenguinException($"Catch variable must be of type __builtin.RuntimeError, but got '{catchDecl.TypeSpecifier.Name}'", catchDecl.SourceLocation, code: ErrorCode.E_TYPE_MISMATCH);
                            container.AddVariableSymbol(catchDecl.Name, true, "__builtin.RuntimeError", catchDecl.SourceLocation, null, false, catchDecl, declaringScopeId: tryStatement.CatchScopeId);
                        }
                        else if (node is TryBindExpression tryBind && tryBind.TypeSpecifier != null)
                        {
                            // Try-bind pattern variable (cast form): register early so
                            // later passes (e.g. async detection) can resolve it when
                            // used as a member-access base (`let a : IFoo := f` then
                            // `a.get()`). Enum-form try-binds (no annotation) are
                            // registered at codegen where the payload type is known.
                            var typeName = tryBind.TypeSpecifier!.Name;
                            container.AddVariableSymbol(tryBind.VariableName!.Name, true, typeName, tryBind.SourceLocation, null, false, null, declaringScopeId: tryBind.ScopeId);
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
                _ = Model.Symbols.Select(s => table.AddRow(s.FullName(), s.TypeInfo, s.IsLocal, s.SourceLocation)).ToList();
                return table.ToMarkDownString();
            }
        }
    }
}