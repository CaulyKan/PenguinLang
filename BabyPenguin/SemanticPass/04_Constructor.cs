namespace BabyPenguin.SemanticPass
{

    public class ConstructorPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is IClass).ToList())
            {
                Process(obj);
            }

            foreach (var obj in Model.FindAll(o => o is IInterface).ToList())
            {
                Process(obj);
            }

            foreach (var obj in Model.FindAll(o => o is INamespace).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            if (obj is IClass cls)
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Class constructor pass for class '{cls.Name}' is skipped now because it is generic");
                }
                else
                {
                    ProcessClass(cls);
                }
            }

            if (obj is IInterface intf)
            {
                if (intf.IsGeneric && !intf.IsSpecialized)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Interface constructor pass for interface '{intf.Name}' is skipped now because it is generic");
                }
                else
                {
                    ProcessInterface(intf);
                }
            }

            if (obj is INamespace ns)
            {
                ProcessNamespace(ns);
            }

            obj.PassIndex = PassIndex;
        }

        public void ProcessNamespace(INamespace ns)
        {
            var sourceLocation = ns.SyntaxNode?.SourceLocation ?? SourceLocation.Empty();

            var constructor = Model.ResolveSymbol(ns.FullName + ".new", checkImportedNamespaces: false) as FunctionSymbol;

            if (constructor == null)
            {
                ns.Constructor = new Function(Model, "new", [], BasicType.Void, sourceLocation, false, false);
                ns.AddFunction(ns.Constructor);
                Model.CatchUp(ns.Constructor);
                constructor = ns.Constructor.FunctionSymbol;
            }

            if (ns.SyntaxNode is NamespaceDefinition syntaxNode)
            {
                foreach (var ev in syntaxNode.Events)
                {
                    InitializeEventDefinition(constructor!.CodeContainer, ns, ev);
                }

                foreach (var decl in syntaxNode.Declarations)
                {
                    if (decl.InitializeExpression != null)
                    {
                        var symbol = Model.ResolveSymbol(decl.Name, scopeDepth: decl.ScopeDepth, scope: ns);
                        constructor!.CodeContainer.AddExpression(decl.InitializeExpression, true, symbol);
                    }
                }
            }
        }

        public void ProcessClass(IClass cls)
        {
            var sourceLocation = cls.SyntaxNode?.SourceLocation ?? SourceLocation.Empty();

            if (cls.Functions.Find(i => i.Name == "new") is IFunction constructorFunc)
            {
                if (constructorFunc.Parameters.Count > 0 &&
                    constructorFunc.Parameters[0].Type.FullName == cls.FullName)
                {
                    cls.Constructor = constructorFunc;
                }
                else
                {
                    throw new BabyPenguinException($"Constructor function of class '{cls.Name}' should have first parameter of type '{cls.FullName}'", sourceLocation);
                }
            }
            else
            {
                List<FunctionParameter> param = [new FunctionParameter("this", cls, false, 0)];
                cls.Constructor = new Function(Model, "new", param, BasicType.Void, sourceLocation, false, false);
                cls.AddFunction(cls.Constructor);
                Model.CatchUp(cls.Constructor);
            }

            if (cls.SyntaxNode is ClassDefinition syntaxNode)
            {
                var constructorBody = (cls.Constructor as ICodeContainer)!;
                foreach (var ev in syntaxNode.Events)
                {
                    InitializeEventDefinition(constructorBody, cls, ev);
                }

                foreach (var varDecl in syntaxNode.Declarations)
                {
                    InitializeVariable(new(cls), constructorBody, varDecl);
                }
            }
        }

        public void ProcessInterface(IInterface intf)
        {
            var sourceLocation = intf.SyntaxNode?.SourceLocation ?? SourceLocation.Empty();

            if (intf.Functions.Find(i => i.Name == "new") is IFunction constructorFunc)
            {
                if (constructorFunc.Parameters.Count > 0 &&
                    constructorFunc.Parameters[0].Type.FullName == intf.FullName)
                {
                    if (constructorFunc.Parameters.Count > 1)
                        throw new BabyPenguinException($"Constructor function of interface '{intf.Name}' should have only one parameter of type '{intf.FullName}'", sourceLocation);
                    intf.Constructor = constructorFunc;
                }
                else
                {
                    throw new BabyPenguinException($"Constructor function of interface '{intf.Name}' should have first parameter of type '{intf.FullName}'", sourceLocation);
                }
            }
            else
            {
                List<FunctionParameter> param = [new FunctionParameter("this", intf, false, 0)];
                intf.Constructor = new Function(Model, "new", param, BasicType.Void, sourceLocation, false, false);
                intf.AddFunction(intf.Constructor);
                Model.CatchUp(intf.Constructor);
            }

            if (intf.SyntaxNode is InterfaceDefinition syntaxNode)
            {
                var constructorBody = (intf.Constructor as ICodeContainer)!;

                foreach (var ev in syntaxNode.Events)
                {
                    InitializeEventDefinition(constructorBody, intf, ev);
                }

                foreach (var varDecl in syntaxNode.Declarations)
                {
                    InitializeVariable(new(intf), constructorBody, varDecl);
                }
            }
        }

        public void InitializeVariable(Or<IInterface, IClass> intfOrCls, ICodeContainer constructorBody, Declaration varDecl)
        {
            if (varDecl.InitializeExpression is ISyntaxExpression initializer)
            {
                var memberSymbol = Model.ResolveShortSymbol(varDecl.Name, scope: intfOrCls.IsLeft ? intfOrCls.Left : intfOrCls.Right)!;
                var thisSymbol = Model.ResolveShortSymbol("this", scope: intfOrCls.IsLeft ? intfOrCls.Left!.Constructor : intfOrCls.Right!.Constructor)!;
                var temp = constructorBody.AddExpression(initializer, true);
                if (temp.TypeInfo.FullName != memberSymbol.TypeInfo.FullName)
                {
                    if (!temp.TypeInfo.CanImplicitlyCastTo(memberSymbol.TypeInfo))
                    {
                        throw new BabyPenguinException($"Cannot assign type '{temp.TypeInfo.FullName}' to type '{memberSymbol.TypeInfo.FullName}'", varDecl.SourceLocation);
                    }
                    else
                    {
                        var castedTemp = constructorBody.AllocTempSymbol(memberSymbol.TypeInfo, varDecl.SourceLocation);
                        constructorBody.AddCastExpression(new(temp), castedTemp, varDecl.SourceLocation);
                        temp = castedTemp;
                    }
                }
                constructorBody.AddInstruction(new WriteMemberInstruction(varDecl.SourceLocation, memberSymbol, temp, thisSymbol));
            }
        }

        public void InitializeEventDefinition(ICodeContainer constructor, ISymbolContainer parent, EventDefinition eventDefinition)
        {
            var symbol = Model.ResolveSymbol(eventDefinition.Name, scopeDepth: eventDefinition.ScopeDepth, scope: parent);
            var initializeExpression = new NewExpression()
            {
                SourceLocation = eventDefinition.SourceLocation,
                ScopeDepth = eventDefinition.ScopeDepth,
                TypeSpecifier = new TypeSpecifier
                {
                    SourceLocation = eventDefinition.SourceLocation,
                    ScopeDepth = eventDefinition.ScopeDepth,
                    TypeName = $"__builtin.Event<{eventDefinition.EventType?.Name ?? "void"}>"
                }
            };
            constructor.AddExpression(initializeExpression, true, symbol);
        }

        public string Report => "";
    }
}