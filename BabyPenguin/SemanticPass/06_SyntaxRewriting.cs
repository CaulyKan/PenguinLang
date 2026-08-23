namespace BabyPenguin.SemanticPass
{
    public class SyntaxRewritingPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process(ISemanticNode node)
        {
            if (node is IFunction function)
            {
                IdentifyAsyncFunction(function);
            }

            if (node is ICodeContainer codeContainer)
            {
                RewriteLambdaFunction(codeContainer);
                RewriteImplicitWait(codeContainer);
                RewriteWaitExpression(codeContainer);
                RewriteAsyncExpression(codeContainer);
            }

            if (node is IFunction function1)
            {
                RewriteGenerator(function1);
            }
        }

        public void Process()
        {
            RegisterVariableNets();
            foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
            {
                IdentifyAsyncFunction(func);
            }

            var codeContainers = Model.FindAll(i => i is ICodeContainer).Cast<ICodeContainer>().ToList();
            foreach (var codeContainer in codeContainers)
            {
                if (codeContainer is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is ITypeNode t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Async rewriting pass for '{codeContainer.FullName()}' is skipped now because it is inside a generic type");
                }
                else
                {
                    RewriteLambdaFunction(codeContainer);
                    RewriteImplicitWait(codeContainer);
                    RewriteWaitExpression(codeContainer);
                    RewriteAsyncExpression(codeContainer);
                    if (codeContainer is IFunction f)
                    {
                        RewriteGenerator(f);
                    }
                }
            }
        }

        public class ClosureSymbolEqualityComparer : IEqualityComparer<ISymbol>
        {
            public bool Equals(ISymbol? x, ISymbol? y)
            {
                if (x == null && y == null)
                    return true;
                if (x == null || y == null)
                    return false;
                return x.Name == y.Name;
            }

            public int GetHashCode(ISymbol obj)
            {
                return obj.Name.GetHashCode();
            }
        }

        private ITypeNode CreateLambdaClass(ICodeContainer codeContainer, CodeBlockExpression codeBlock, List<FunctionParameter> parameters, IType returnType,
            List<ISymbol> closureSymbols, SourceLocation sourceLocation, bool isStatic, bool isAsync)
        {
            ITypeContainer typeContainer = codeContainer.FindAncestorIncludingSelf(i => i is ITypeContainer) as ITypeContainer ??
                throw new BabyPenguinException($"Parent is not a type container for lambda function", sourceLocation, code: ErrorCode.E_INTERNAL);

            var lambdaClass = typeContainer.AddLambdaClass(
                codeContainer.Name,
                codeBlock,
                parameters,
                returnType,
                closureSymbols,
                sourceLocation,
                isStatic,
                isAsync
            );

            Model.Reporter.Write(DiagnosticLevel.Debug, $"Created lambda class `{lambdaClass.FullName()}`");
            AddRewritedSource(lambdaClass.FullName(), Tools.FormatPenguinLangSource(lambdaClass.SyntaxNode!.BuildText()));

            return lambdaClass;
        }

        private List<ISymbol> CollectClosureSymbols(CodeBlockExpression codeBlock, ICodeContainer codeContainer)
        {
            var closureSymbols = new List<ISymbol>();
            codeBlock.TraverseChildren((node, parent) =>
            {
                if (node is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                {
                    var localSymbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                        s => s.IsLocal, scope: codeContainer);

                    if (localSymbol != null && IsFromOuterScope(localSymbol, codeBlock))
                    {
                        closureSymbols.Add(localSymbol);
                    }
                }
                return true;
            });

            closureSymbols = closureSymbols.Distinct(new ClosureSymbolEqualityComparer()).ToList();
            Model.Reporter.Write(DiagnosticLevel.Debug, $"Found closure symbols: {string.Join(", ", closureSymbols.Select(i => i.Name))}");

            return closureSymbols;
        }

        public void RewriteLambdaFunction(ICodeContainer codeContainer)
        {
            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is LambdaFunctionExpression lambdaFunctionExpression)
                {
                    var parameters = lambdaFunctionExpression.Parameters.Select((p, i) => new FunctionParameter(
                        p.Name,
                        Model.ResolveType(p.TypeSpecifier!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve parameter type '{p.TypeSpecifier.Name}'", p.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE),
                        i
                    )).ToList();

                    var returnType = Model.ResolveType(lambdaFunctionExpression.ReturnType!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve return type '{lambdaFunctionExpression.ReturnType.Name}'", lambdaFunctionExpression.ReturnType.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);

                    var closureSymbols = CollectClosureSymbols(lambdaFunctionExpression.CodeBlockExpression!, codeContainer);

                    var lambdaClass = CreateLambdaClass(
                        codeContainer,
                        lambdaFunctionExpression.CodeBlockExpression!,
                        parameters,
                        returnType,
                        closureSymbols,
                        lambdaFunctionExpression.SourceLocation,
                        false, // isStatic
                        lambdaFunctionExpression.IsAsync
                    );

                    var newExp = new ReadMemberAccessExpression();
                    var closureArgs = string.Join(", ", closureSymbols.Select(i => i.Name));
                    newExp.FromString($"(new {lambdaClass.FullName()}({closureArgs})).call", Model.Reporter);

                    if (parent is PrimaryExpression expr)
                    {
                        expr.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                        expr.ParenthesizedExpression = newExp;
                        expr.LambdaFunction = null;
                    }
                    else
                    {
                        throw new BabyPenguinException($"Unexpected parent type for lambda function expression: {parent.GetType().Name}", lambdaFunctionExpression.SourceLocation, code: ErrorCode.E_INTERNAL);
                    }

                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Rewrote lambda function to class `{lambdaClass.FullName()}`");
                    AddRewritedSource(codeContainer.FullName(), Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
                }
                return true;
            });
        }

        public void RewriteImplicitWait(ICodeContainer codeContainer)
        {
            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is FunctionCallExpression exp)
                {
                    // Best-effort wait detection: skip calls whose callee can't be
                    // resolved yet (e.g. a try-bind pattern variable registered at
                    // codegen). Real resolution happens in the CodeGeneration pass.
                    ISymbol? symbol;
                    try
                    {
                        var callee = exp.Callee!.GetEffectiveExpression();
                        if (callee is MemberAccessExpression memberAccess)
                        {
                            codeContainer.ResolveMemberAccessExpressionSymbol(memberAccess, out _, out symbol);
                        }
                        else if (callee is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                        {
                            symbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                                s => !s.IsClassMember, scope: codeContainer);
                        }
                        else
                        {
                            return true;
                        }
                    }
                    catch (BabyPenguinException)
                    {
                        return true;
                    }

                    if (symbol == null || !symbol.IsFunction) return true;

                    var isAsync = false;
                    if (symbol is FunctionSymbol callingFunc)
                    {
                        if (callingFunc.CodeContainer is IFunction func)
                        {
                            if (func.IsGenerator) isAsync = false;
                            else isAsync = func.IsAsync ?? false;
                        }
                        else throw new NotImplementedException();
                    }
                    else if (symbol is FunctionVariableSymbol functionVariableSymbol)
                    {
                        isAsync = functionVariableSymbol.IsAsync;
                    }

                    if (isAsync)
                    {
                        if (parent is WaitExpression)
                        {
                            // OK, explicit wait
                        }
                        else if (parent is SpawnAsyncExpression)
                        {
                            // OK, explicit async
                        }
                        else
                        {
                            (parent as ISyntaxNode).ReplaceChild(node, node.Build<WaitExpression>(e =>
                            {
                                e.Expression = exp;
                            }));
                            Model.Reporter.Write(DiagnosticLevel.Debug, $"Added implicit wait for function call: {exp}", exp.SourceLocation);
                        }
                    }
                }
                return true;
            });
        }

        /// <summary>
        /// Implicit wire nets: a `mut` variable used as a connect source
        /// (connect(x, f.in) / connect(this.f, inner.in)) gets a hidden
        /// _Fanout hub variable registered here — BEFORE any body binding,
        /// so assignment compilation (pass 07) can consult the registry no
        /// matter the definition order. Top-level construct lets hoist to
        /// the namespace (hub = namespace variable, writable from any
        /// initial); class fields get a hidden class field. Class-construct
        /// locals are rejected: they die when the wiring block returns, so a
        /// net through them could never fire after elaboration.
        /// </summary>
        public void RegisterVariableNets()
        {
            foreach (var container in Model.FindAll(i => i is IFunction).Cast<IFunction>())
            {
                var isTopConstruct = container.Name.StartsWith(SemanticScopingPass.ConstructPrefix) && container.Parent is INamespace;
                var isClassConstruct = container.Name.StartsWith(SemanticScopingPass.ClassConstructPrefix) && container.Parent is ITypeNode;
                if (!isTopConstruct && !isClassConstruct) continue;
                if (container is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is ITypeNode t && t.IsGeneric && !t.IsSpecialized) != null)
                    continue;

                var ownDeclarations = new HashSet<Declaration>();
                container.CodeSyntaxNode?.TraverseChildren((node, _) =>
                {
                    if (node is Declaration d)
                        ownDeclarations.Add(d);
                    return true;
                });

                container.CodeSyntaxNode?.TraverseChildren((node, _) =>
                {
                    if (node is Statement { StatementType: Statement.Type.ConnectStatement } stmt)
                    {
                        var conn = stmt.ConnectStatement!;
                        if (conn.Source is PrimaryExpression { PrimaryExpressionType: PrimaryExpression.Type.Identifier } pe)
                            RegisterNet(container, pe.Identifier!.Name, pe.SourceLocation, isField: false, ownDeclarations);
                        else if (conn.Source is MemberAccessExpression srcMa
                                 && srcMa.BaseExpression is PrimaryExpression { PrimaryExpressionType: PrimaryExpression.Type.Identifier } basePe
                                 && basePe.Identifier!.Name == "this")
                            RegisterNet(container, srcMa.Member!.Name, srcMa.SourceLocation, isField: true, ownDeclarations);
                    }
                    return true;
                });
            }
        }

        /// <summary>
        /// Channel-like types (implement ISource/ISink/IChannel) bind DIRECTLY
        /// as connect sources — implicit nets are for payload-carrying mut
        /// variables (i64, bool, ...), not for channels themselves. Events
        /// likewise bind through their own wire mechanism (connect_wire).
        /// </summary>
        private static bool IsChannelLikeType(IType type)
        {
            if (type.TypeNode?.GenericType?.FullName() == "__builtin.Event<?>")
                return true;
            if (type.TypeNode is not IVTableContainer vt) return false;
            return vt.ImplementedInterfaces.Any(i =>
                i.FullName().StartsWith("__builtin.ISource<")
                || i.FullName().StartsWith("__builtin.ISink<")
                || i.FullName().StartsWith("__builtin.IChannel<"));
        }

        private void RegisterNet(IFunction constructFunc, string name, SourceLocation location, bool isField, HashSet<Declaration> ownDeclarations)
        {
            if (isField)
            {
                // Class field source: connect(this.f, ...) — hub is a hidden field.
                var cls = (ISymbolContainer)constructFunc.Parent!;
                var clsName = ((ISemanticNode)cls).FullName();
                var fieldSym = Model.ResolveSymbol($"{clsName}.{name}")
                    ?? throw new BabyPenguinException($"Cant resolve field '{name}' of class '{clsName}'", location, code: ErrorCode.E_RESOLVE_SYMBOL);
                fieldSym = UnwrapSymbol(fieldSym);
                if (IsChannelLikeType(fieldSym.TypeInfo)) return;
                if (Model.VariableNets.ContainsKey(fieldSym)) return;
                if (fieldSym.TypeInfo.IsMutable != Mutability.Mutable)
                    throw new BabyPenguinException($"connect source '{name}' must be a mut field (implicit wire nets need mutable storage)", location, code: ErrorCode.E_MUTABILITY);
                var payload = fieldSym.TypeInfo.WithMutability(Mutability.Auto);
                var hubName = $"__net_{Model.VariableNetCounter++}_{name}";
                var hubSym = cls.AddVariableSymbol(hubName, false, new Or<string, IType>($"__builtin._Fanout<{payload.FullName()}>"), location, null, true, null, Mutability.Mutable);
                Model.VariableNets[fieldSym] = hubSym;
                Model.Reporter.Write(DiagnosticLevel.Debug, $"Registered implicit net for field '{clsName}.{name}' (hub '{hubName}')");
            }
            else
            {
                // Top-level construct let: hoisted to the namespace by pass 03.
                var ns = (INamespace)constructFunc.Parent!;
                var varSym = Model.ResolveShortSymbol(name, scope: ns)
                    ?? throw new BabyPenguinException($"Cant resolve symbol '{name}'", location, code: ErrorCode.E_RESOLVE_SYMBOL);
                varSym = UnwrapSymbol(varSym);
                if (IsChannelLikeType(varSym.TypeInfo)) return;
                if (Model.VariableNets.ContainsKey(varSym)) return;
                if (varSym is not VariableSymbol decl || decl.Declaration?.TypeSpecifier == null)
                    throw new BabyPenguinException($"connect source '{name}' is a variable net and needs an explicit mut type annotation (let {name} : mut T = ...)", location, code: ErrorCode.E_TYPE_MISMATCH);
                // Only lets declared INSIDE this construct block become nets —
                // a top-level global referenced by the wiring is not a net
                // candidate (nets need the hub initialized during elaboration).
                if (!ownDeclarations.Contains(decl.Declaration))
                    throw new BabyPenguinException($"connect source '{name}' must be declared inside the construct block (implicit nets are for construct lets and class fields)", location, code: ErrorCode.E_TYPE_MISMATCH);
                var payloadType = Model.ResolveType(decl.Declaration.TypeSpecifier.Name, scope: ns)
                    ?? throw new BabyPenguinException($"Cant resolve type '{decl.Declaration.TypeSpecifier.Name}'", location, code: ErrorCode.E_RESOLVE_TYPE);
                if (payloadType.IsMutable != Mutability.Mutable)
                    throw new BabyPenguinException($"connect source '{name}' must be a mut variable (implicit wire nets need mutable storage)", location, code: ErrorCode.E_MUTABILITY);
                var payload = payloadType.WithMutability(Mutability.Auto);
                var hubName = $"__net_{Model.VariableNetCounter++}_{name}";
                var hubSym = ns.AddVariableSymbol(hubName, false, new Or<string, IType>($"__builtin._Fanout<{payload.FullName()}>"), location, null, false, null);
                Model.VariableNets[varSym] = hubSym;
                Model.Reporter.Write(DiagnosticLevel.Debug, $"Registered implicit net for '{ns.FullName()}.{name}' (hub '{hubName}')");
            }
        }

        private static ISymbol UnwrapSymbol(ISymbol symbol)
            => symbol is MutableSymbolProxy proxy ? proxy.Symbol : symbol;

        public void RewriteWaitExpression(ICodeContainer codeContainer)
        {
            /*
                async fun test() -> bool {}

                initial {
                    var a : bool = wait test();
                }

                // rewrite to:
                initial {
                    var a : bool = (async test()).wait();
                }
            */

            if ((codeContainer is IInitialRoutine) || (codeContainer is IFunction f && f.IsAsync == true))
            {
                codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is WaitExpression waitExp && waitExp.Expression != null)
                    {
                        RewriteWaitExpression(codeContainer, parent, waitExp);
                    }
                    return true;
                });
            }
        }

        private void RewriteWaitExpression(ICodeContainer codeContainer, SyntaxNode parent, WaitExpression waitExpression)
        {
            if (waitExpression.Expression is null) return;

            // `wait change(x)` is instruction-level sugar (edge detection in
            // the WaitExpression compile) — nothing to rewrite here, and its
            // `change(x)` operand must NOT be resolved as a call.
            if (waitExpression.IsChangeUnit) return;

            if (waitExpression.IsTickUnit)
            {
                // wait <expr> tick -> __builtin._after(<expr>)
                var callExp = waitExpression.Build<FunctionCallExpression>(e =>
                {
                    e.FromString($"__builtin._after({waitExpression.Expression.BuildText()})", Model.Reporter);
                });
                waitExpression.Expression = callExp;
                waitExpression.IsTickUnit = false;
                AddRewritedSource(codeContainer.FullName(), Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
                return;
            }

            var expression = (SyntaxNode)waitExpression.Expression;

            SyntaxNode futureExp;

            var waitType = codeContainer.ResolveExpressionType(waitExpression.Expression);
            if (waitType.TypeNode!.GenericType != null && waitType.TypeNode!.GenericType.FullName() == "__builtin.Event<?>")
            {
                var newExp = waitExpression.Build<NewExpression>(e =>
                {
                    e.TypeSpecifier = new TypeSpecifier { TypeName = $"__builtin._EventSubscription<{waitType.GenericArguments.First()}>" };
                    e.ArgumentsExpression = [waitExpression.Expression];
                });
                futureExp = newExp;

                var primaryExp = futureExp.Build<PrimaryExpression>(e =>
                {
                    e.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                    e.ParenthesizedExpression = futureExp as ISyntaxExpression;
                });

                waitExpression.Expression = primaryExp;
                Model.Reporter.Write(DiagnosticLevel.Debug, $"rewriting wait expression: '{expression}' to '{primaryExp}'", expression.SourceLocation);
                AddRewritedSource(codeContainer.FullName(), Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
            }
        }

        private void RewriteAsyncExpression(ICodeContainer codeContainer)
        {
            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is SpawnAsyncExpression spawnAsyncExp)
                {
                    if (spawnAsyncExp.Expression is FunctionCallExpression funcCallExp && funcCallExp.ArgumentsExpression.Count > 0)
                    {
                        var codeBlock = new CodeBlockExpression
                        {
                            ScopeId = spawnAsyncExp.ScopeId,
                            SourceLocation = spawnAsyncExp.SourceLocation
                        };
                        var statement = new Statement
                        {
                            StatementType = Statement.Type.ReturnStatement,
                            ReturnStatement = new ReturnStatement
                            {
                                ReturnExpression = funcCallExp,
                                ScopeId = spawnAsyncExp.ScopeId,
                                SourceLocation = spawnAsyncExp.SourceLocation
                            },
                            ScopeId = spawnAsyncExp.ScopeId,
                            SourceLocation = spawnAsyncExp.SourceLocation
                        };

                        var codeBlockItem = new CodeBlockItem();
                        codeBlockItem.FromString(statement.BuildText(), Model.Reporter);

                        codeBlock.BlockItems.Add(codeBlockItem);

                        var closureSymbols = CollectClosureSymbols(codeBlock, codeContainer);

                        var returnType = codeContainer.ResolveExpressionType(funcCallExp);

                        var lambdaClass = CreateLambdaClass(
                            codeContainer,
                            codeBlock,
                            [],
                            returnType,
                            closureSymbols,
                            spawnAsyncExp.SourceLocation,
                            false, // isStatic
                            true // isAsync
                        );

                        var newExp = new FunctionCallExpression();
                        var closureArgs = string.Join(", ", closureSymbols.Select(i => i.Name));
                        newExp.FromString($"(new {lambdaClass.FullName()}({closureArgs})).call()", Model.Reporter);

                        spawnAsyncExp.Expression = newExp;

                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Rewrote async expression to class `{lambdaClass.FullName()}`");
                        AddRewritedSource(codeContainer.FullName(), Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
                    }
                }
                return true;
            });
        }

        public void IdentifyAsyncFunction(IFunction func)
        {
            if (func.FullName().Contains('?')) return;

            var isAsyncKnown = func.IsAsync != null;

            if (!isAsyncKnown) func.IsAsync = false;
            func.SyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is WaitExpression)
                    {
                        if (!isAsyncKnown) func.IsAsync = true;
                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Mark function {func.FullName()} as async because it has wait statement", node.SourceLocation);
                        return false;
                    }
                    else if (node is FunctionCallExpression exp && !isAsyncKnown)
                    {
                        // Async detection is best-effort: skip calls whose callee
                        // can't be resolved yet (e.g. a try-bind pattern variable
                        // that is only registered at codegen). The real callee
                        // resolution happens in the CodeGeneration pass.
                        ISymbol? symbol;
                        try
                        {
                            var callee = exp.Callee!.GetEffectiveExpression();
                            if (callee is MemberAccessExpression memberAccess)
                            {
                                func.ResolveMemberAccessExpressionSymbol(memberAccess, out _, out symbol);
                            }
                            else if (callee is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                            {
                                symbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                                    s => !s.IsClassMember, scope: func);
                            }
                            else
                            {
                                return true; // unknown callee shape — skip
                            }
                        }
                        catch (BabyPenguinException)
                        {
                            return true; // unresolvable callee — skip async detection for it
                        }

                        if (symbol == null || !symbol.IsFunction) return true; // skip non-function callees

                        if (symbol is MutableSymbolProxy proxy)
                            symbol = proxy.Symbol;

                        if (symbol is FunctionSymbol callingFuncSymbol)
                        {
                            var callingFunc = callingFuncSymbol.CodeContainer as IFunction;
                            if (callingFunc == null) throw new BabyPenguinException($"Can't resolve function symbol context {exp}", exp.SourceLocation, code: ErrorCode.E_RESOLVE_SYMBOL);

                            if (callingFunc.IsAsync == null)
                            {
                                IdentifyAsyncFunction(callingFunc);
                            }
                            if (callingFunc.IsAsync == true)
                            {
                                func.IsAsync = true;
                                Model.Reporter.Write(DiagnosticLevel.Debug, $"Mark function {func.FullName()} as async because it calls async function '{callingFunc.FullName()}'", exp.SourceLocation);
                                return false;
                            }
                        }
                        else if (symbol is FunctionVariableSymbol callingFunctionVariableSymbol)
                        {
                            if (callingFunctionVariableSymbol.IsAsync == true)
                            {
                                func.IsAsync = true;
                                Model.Reporter.Write(DiagnosticLevel.Debug, $"Mark function {func.FullName()} as async because it calls async function '{callingFunctionVariableSymbol.FullName()}'", exp.SourceLocation);
                                return false;
                            }
                        }
                        else throw new NotImplementedException();
                    }
                    return true;
                });
        }

        public void RewriteGenerator(IFunction func)
        {
            if (func.IsGenerator == true)
            {
                if (func.Parent is not ITypeContainer typeContainer) throw new BabyPenguinException($"Parent is not a type container: {func.FullName()}", null, code: ErrorCode.E_INTERNAL);

                IType? returnType = null;

                if (func.ReturnTypeInfo.TypeNode!.GenericType?.FullName() == "__builtin.IGenerator<?>" && func.ReturnTypeInfo.GenericArguments.FirstOrDefault() is IType t)
                    returnType = t;
                else if (func.ReturnTypeInfo.IsVoidType)
                    returnType = Model.BasicTypeNodes.Void.ToType(Mutability.Auto);
                else throw new BabyPenguinException($"Generator function '{func.FullName()}' return type should be an iterator or void", null, code: ErrorCode.E_YIELD_CONTEXT);

                if (func.SyntaxNode is FunctionDefinition functionDefinition)
                {
                    functionDefinition.CodeBlockExpression?.TraverseChildren((node, parent) =>
                    {
                        if (node is YieldStatement yieldStatement)
                        {
                            if (parent is Statement statement)
                            {
                                statement.ReturnStatement = new ReturnStatement
                                {
                                    ReturnExpression = yieldStatement.YieldExpression,
                                    ReturnType = ReturnStatement.ReturnTypeEnum.YieldNotFinished,
                                    ScopeId = node.ScopeId,
                                    SourceLocation = node.SourceLocation
                                };
                                statement.StatementType = Statement.Type.ReturnStatement;
                            }
                            else throw new NotImplementedException();
                        }
                        return true;
                    });

                    var lambdaClass = typeContainer.AddLambdaClass(func.Name, functionDefinition.CodeBlockExpression, func.Parameters, returnType, [], func.SourceLocation.StartLocation, false, func.IsAsync);
                    var cb = new CodeBlockExpression();
                    cb.FromString(@$"
                            {{
                                let owner: mut {lambdaClass.Name} = new {lambdaClass.Name}();
                                return cast<__builtin.IGenerator<{returnType.FullName()}>>(new __builtin._DefaultRoutine<{returnType.FullName()}>(owner.call, true));
                            }}
                        ", Model.Reporter);
                    functionDefinition.CodeBlockExpression = cb;
                    Model.GetPass<SymbolElaboratePass>().ElaborateLocalSymbol(func);

                    Model.Reporter.Write(DiagnosticLevel.Debug, $"rewrite generator function `{func.FullName()}` to `{lambdaClass.Name}`");
                    AddRewritedSource(func.FullName(), Tools.FormatPenguinLangSource(func.SyntaxNode.BuildText()));
                }
                else
                {
                    Model.Reporter.Write(DiagnosticLevel.Warning, $"skip rewrite generator function `{func.FullName()}` because no syntax node is found.");
                }
            }
        }

        public Dictionary<string, string> RewritedSource { get; } = [];

        /// <summary>
        /// Checks if a local symbol was declared in a scope that is a STRICT ancestor
        /// of the codeBlock's scope (not the codeBlock's own scope). Returns true if the
        /// symbol is from an outer scope (closure needed).
        /// </summary>
        private static bool IsFromOuterScope(ISymbol localSymbol, SyntaxNode codeBlock)
        {
            if (localSymbol is VariableSymbol varSymbol && varSymbol.DeclaringScopeId != 0)
            {
                uint blockScopeId = codeBlock.ScopeId;
                // The symbol must be from a STRICT ancestor, not the same scope.
                // Walk up from the codeBlock's parent (skip the codeBlock itself).
                uint currentId = blockScopeId;
                if (!SyntaxWalker.ScopeParentMap.TryGetValue(currentId, out var parentId))
                    return false; // No parent - symbol is not from an outer scope
                currentId = parentId;

                while (currentId != 0)
                {
                    if (currentId == varSymbol.DeclaringScopeId)
                        return true; // Found in strict ancestor chain
                    if (!SyntaxWalker.ScopeParentMap.TryGetValue(currentId, out var nextParent))
                        break;
                    currentId = nextParent;
                }
                return false; // Not found in ancestor chain
            }
            return true; // No scope info, assume it's from outer scope
        }

        private void AddRewritedSource(string fullName, string source)
        {
            RewritedSource.Remove(fullName);
            RewritedSource.Add(fullName, source);
            Model.Reporter.Write(DiagnosticLevel.Debug, $"Rewrited source for {fullName}: \n{source}");
        }

        public string Report
        {
            get
            {
                var sb = new StringBuilder();
                var table = new ConsoleTable("Function", "IsAsync");
                foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
                {
                    table.AddRow(func.FullName(), func.IsAsync);
                }
                sb.AppendLine(table.ToMarkDownString());
                return sb.ToString();
            }
        }
    }
}