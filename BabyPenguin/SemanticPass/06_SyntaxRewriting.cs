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
            foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
            {
                IdentifyAsyncFunction(func);
            }

            var codeContainers = Model.FindAll(i => i is ICodeContainer).Cast<ICodeContainer>().ToList();
            foreach (var codeContainer in codeContainers)
            {
                if (codeContainer is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is IType t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Async rewriting pass for '{codeContainer.FullName}' is skipped now because it is inside a generic type");
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

        private IType CreateLambdaClass(ICodeContainer codeContainer, CodeBlock codeBlock, List<FunctionParameter> parameters, IType returnType,
            List<ISymbol> closureSymbols, SourceLocation sourceLocation, uint scopeDepth, bool isStatic, bool returnValueIsReadonly, bool isAsync)
        {
            ITypeContainer typeContainer = codeContainer.FindAncestorIncludingSelf(i => i is ITypeContainer) as ITypeContainer ??
                throw new BabyPenguinException($"Parent is not a type container for lambda function", sourceLocation);

            var lambdaClass = typeContainer.AddLambdaClass(
                codeContainer.Name,
                codeBlock,
                parameters,
                returnType,
                closureSymbols,
                sourceLocation,
                scopeDepth,
                isStatic,
                returnValueIsReadonly,
                isAsync
            );

            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Created lambda class `{lambdaClass.FullName}`");
            AddRewritedSource(lambdaClass.FullName, Tools.FormatPenguinLangSource(lambdaClass.SyntaxNode!.BuildText()));

            return lambdaClass;
        }

        private List<ISymbol> CollectClosureSymbols(CodeBlock codeBlock, ICodeContainer codeContainer)
        {
            var closureSymbols = new List<ISymbol>();
            codeBlock.TraverseChildren((node, parent) =>
            {
                if (node is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                {
                    var localSymbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                        s => s.IsLocal, scopeDepth: primaryExp.ScopeDepth, scope: codeContainer);

                    if (localSymbol != null && localSymbol.ScopeDepth < codeBlock.ScopeDepth)
                    {
                        closureSymbols.Add(localSymbol);
                    }
                }
                return true;
            });

            closureSymbols = closureSymbols.Distinct(new ClosureSymbolEqualityComparer()).ToList();
            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Found closure symbols: {string.Join(", ", closureSymbols.Select(i => i.Name))}");

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
                        Model.ResolveType(p.TypeSpecifier!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve parameter type '{p.TypeSpecifier.Name}'", p.SourceLocation),
                        p.IsReadonly,
                        i
                    )).ToList();

                    var returnType = Model.ResolveType(lambdaFunctionExpression.ReturnType!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve return type '{lambdaFunctionExpression.ReturnType.Name}'", lambdaFunctionExpression.ReturnType.SourceLocation);

                    var closureSymbols = CollectClosureSymbols(lambdaFunctionExpression.CodeBlock!, codeContainer);

                    var lambdaClass = CreateLambdaClass(
                        codeContainer,
                        lambdaFunctionExpression.CodeBlock!,
                        parameters,
                        returnType,
                        closureSymbols,
                        lambdaFunctionExpression.SourceLocation,
                        lambdaFunctionExpression.ScopeDepth,
                        false, // isStatic
                        lambdaFunctionExpression.ReturnValueIsReadonly ?? false,
                        lambdaFunctionExpression.IsAsync
                    );

                    var newExp = new ReadMemberAccessExpression();
                    var closureArgs = string.Join(", ", closureSymbols.Select(i => i.Name));
                    newExp.FromString($"(new {lambdaClass.FullName}({closureArgs})).call", lambdaFunctionExpression.ScopeDepth, Model.Reporter);

                    if (parent is PrimaryExpression expr)
                    {
                        expr.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                        expr.ParenthesizedExpression = newExp;
                        expr.LambdaFunction = null;
                    }
                    else
                    {
                        throw new BabyPenguinException($"Unexpected parent type for lambda function expression: {parent.GetType().Name}", lambdaFunctionExpression.SourceLocation);
                    }

                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Rewrote lambda function to class `{lambdaClass.FullName}`");
                    AddRewritedSource(codeContainer.FullName, Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
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
                    ISymbol? symbol;
                    if (exp.IsMemberAccess)
                    {
                        codeContainer.ResolveMemberAccessExpressionSymbol(exp.MemberAccessExpression!, out _, out symbol);
                    }
                    else
                    {
                        if (exp.PrimaryExpression?.GetEffectiveExpression() is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                        {
                            symbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                                s => !s.IsClassMember, scopeDepth: exp.ScopeDepth, scope: codeContainer);
                        }
                        else
                        {
                            throw new NotImplementedException(); // TODO: Handle other types of expressions
                        }
                    }

                    if (symbol == null || !symbol.IsFunction) throw new BabyPenguinException($"Can't resolve function symbol {exp}", exp.SourceLocation);

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
                            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Added implicit wait for function call: {exp}", exp.SourceLocation);
                        }
                    }
                }
                return true;
            });
        }

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
            var expression = (SyntaxNode)waitExpression.Expression;

            SyntaxNode futureExp;

            var waitType = codeContainer.ResolveExpressionType(waitExpression.Expression);
            if (waitType.GenericType != null && waitType.GenericType.FullName == "__builtin.Event<?>")
            {
                var newExp = waitExpression.Build<NewExpression>(e =>
                {
                    e.TypeSpecifier = new TypeSpecifier { TypeName = $"__builtin._OnetimeEventReceiver<{waitType.GenericArguments.First()}>" };
                    e.ArgumentsExpression = [waitExpression.Expression];
                });
                futureExp = newExp;

                var primaryExp = futureExp.Build<PrimaryExpression>(e =>
                {
                    e.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                    e.ParenthesizedExpression = futureExp as ISyntaxExpression;
                });

                waitExpression.Expression = primaryExp;
                Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"rewriting wait expression: '{expression}' to '{primaryExp}'", expression.SourceLocation);
                AddRewritedSource(codeContainer.FullName, Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
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
                        var codeBlock = new CodeBlock
                        {
                            ScopeDepth = spawnAsyncExp.ScopeDepth,
                            SourceLocation = spawnAsyncExp.SourceLocation
                        };
                        var statement = new Statement
                        {
                            StatementType = Statement.Type.ReturnStatement,
                            ReturnStatement = new ReturnStatement
                            {
                                ReturnExpression = funcCallExp,
                                ScopeDepth = spawnAsyncExp.ScopeDepth,
                                SourceLocation = spawnAsyncExp.SourceLocation
                            },
                            ScopeDepth = spawnAsyncExp.ScopeDepth,
                            SourceLocation = spawnAsyncExp.SourceLocation
                        };

                        var codeBlockItem = new CodeBlockItem();
                        codeBlockItem.FromString(statement.BuildText(), spawnAsyncExp.ScopeDepth, Model.Reporter);

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
                            spawnAsyncExp.ScopeDepth,
                            false, // isStatic
                            false, // returnValueIsReadonly
                            true // isAsync
                        );

                        var newExp = new FunctionCallExpression();
                        var closureArgs = string.Join(", ", closureSymbols.Select(i => i.Name));
                        newExp.FromString($"(new {lambdaClass.FullName}({closureArgs})).call()", spawnAsyncExp.ScopeDepth, Model.Reporter);

                        spawnAsyncExp.Expression = newExp;

                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Rewrote async expression to class `{lambdaClass.FullName}`");
                        AddRewritedSource(codeContainer.FullName, Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildText()));
                    }
                }
                return true;
            });
        }

        public void IdentifyAsyncFunction(IFunction func)
        {
            if (func.FullName.Contains('?')) return;

            var isAsyncKnown = func.IsAsync != null;

            if (!isAsyncKnown) func.IsAsync = false;
            func.SyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is WaitExpression)
                    {
                        if (!isAsyncKnown) func.IsAsync = true;
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Mark function {func.FullName} as async because it has wait statement", node.SourceLocation);
                        return false;
                    }
                    else if (node is FunctionCallExpression exp && !isAsyncKnown)
                    {
                        ISymbol? symbol;
                        if (exp.IsMemberAccess)
                        {
                            func.ResolveMemberAccessExpressionSymbol(exp.MemberAccessExpression!, out _, out symbol);
                        }
                        else
                        {
                            if (exp.PrimaryExpression!.GetEffectiveExpression() is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                            {
                                symbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                                    s => !s.IsClassMember, scopeDepth: exp.ScopeDepth, scope: func);
                            }
                            else
                            {
                                throw new NotImplementedException(); // TODO: Handle other types of expressions
                            }
                        }

                        if (symbol == null || !symbol.IsFunction) throw new BabyPenguinException($"Can't resolve function symbol {exp}", exp.SourceLocation);

                        if (symbol is FunctionSymbol callingFuncSymbol)
                        {
                            var callingFunc = callingFuncSymbol.CodeContainer as IFunction;
                            if (callingFunc == null) throw new BabyPenguinException($"Can't resolve function symbol context {exp}", exp.SourceLocation);

                            if (callingFunc.IsAsync == null)
                            {
                                IdentifyAsyncFunction(callingFunc);
                            }
                            if (callingFunc.IsAsync == true)
                            {
                                func.IsAsync = true;
                                Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Mark function {func.FullName} as async because it calls async function '{callingFunc.FullName}'", exp.SourceLocation);
                                return false;
                            }
                        }
                        else if (symbol is FunctionVariableSymbol callingFunctionVariableSymbol)
                        {
                            if (callingFunctionVariableSymbol.IsAsync == true)
                            {
                                func.IsAsync = true;
                                Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Mark function {func.FullName} as async because it calls async function '{callingFunctionVariableSymbol.FullName}'", exp.SourceLocation);
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
                if (func.Parent is not ITypeContainer typeContainer) throw new BabyPenguinException($"Parent is not a type container: {func.FullName}");

                IType? returnType = null;

                if (func.ReturnTypeInfo.GenericType?.FullName == "__builtin.IGenerator<?>" && func.ReturnTypeInfo.GenericArguments.FirstOrDefault() is IType t)
                    returnType = t;
                else if (func.ReturnTypeInfo.IsVoidType)
                    returnType = BasicType.Void;
                else throw new BabyPenguinException($"Generator function '{func.FullName}' return type should be an iterator or void");

                if (func.SyntaxNode is FunctionDefinition functionDefinition)
                {
                    functionDefinition.CodeBlock?.TraverseChildren((node, parent) =>
                    {
                        if (node is YieldStatement yieldStatement)
                        {
                            if (parent is Statement statement)
                            {
                                statement.ReturnStatement = new ReturnStatement
                                {
                                    ReturnExpression = yieldStatement.YieldExpression,
                                    ReturnType = ReturnStatement.ReturnTypeEnum.YieldNotFinished,
                                    ScopeDepth = node.ScopeDepth,
                                    SourceLocation = node.SourceLocation
                                };
                                statement.StatementType = Statement.Type.ReturnStatement;
                            }
                            else throw new NotImplementedException();
                        }
                        return true;
                    });

                    var lambdaClass = typeContainer.AddLambdaClass(func.Name, functionDefinition.CodeBlock, func.Parameters, returnType, [], func.SourceLocation.StartLocation, functionDefinition.ScopeDepth, false, false, func.IsAsync);
                    var cb = new CodeBlock();
                    cb.FromString(@$"
                            {{
                                var owner: {lambdaClass.Name} = new {lambdaClass.Name}();
                                return new __builtin._DefaultRoutine<{returnType.FullName}>(owner.call, true) as __builtin.IGenerator<{returnType.FullName}>;
                            }}
                        ", functionDefinition.ScopeDepth + 1, Model.Reporter);
                    functionDefinition.CodeBlock = cb;
                    Model.GetPass<SymbolElaboratePass>().ElaborateLocalSymbol(func);

                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"rewrite generator function `{func.FullName}` to `{lambdaClass.Name}`");
                    AddRewritedSource(func.FullName, Tools.FormatPenguinLangSource(func.SyntaxNode.BuildText()));
                }
                else
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Warning, $"skip rewrite generator function `{func.FullName}` because no syntax node is found.");
                }
            }
        }

        public Dictionary<string, string> RewritedSource { get; } = [];
        private void AddRewritedSource(string fullName, string source)
        {
            RewritedSource.Remove(fullName);
            RewritedSource.Add(fullName, source);
            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Rewrited source for {fullName}: \n{source}");
        }

        public string Report
        {
            get
            {
                var sb = new StringBuilder();
                var table = new ConsoleTable("Function", "IsAsync");
                foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
                {
                    table.AddRow(func.FullName, func.IsAsync);
                }
                sb.AppendLine(table.ToMarkDownString());
                return sb.ToString();
            }
        }
    }
}