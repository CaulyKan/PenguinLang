using System.Security.Cryptography;

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
                RewriteAsyncWait(codeContainer);
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
                    RewriteAsyncWait(codeContainer);
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

        public void RewriteLambdaFunction(ICodeContainer codeContainer)
        {
            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is LambdaFunctionExpression lambdaFunctionExpression)
                {
                    ITypeContainer typeContainer = codeContainer.Parent as ITypeContainer ?? codeContainer.Parent?.Parent as ITypeContainer ??
                        throw new BabyPenguinException($"Parent is not a type container for lambda function", lambdaFunctionExpression.SourceLocation);

                    // Convert parameters to FunctionParameter list
                    var parameters = lambdaFunctionExpression.Parameters.Select((p, i) => new FunctionParameter(
                        p.Name,
                        Model.ResolveType(p.TypeSpecifier!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve parameter type '{p.TypeSpecifier.Name}'", p.SourceLocation),
                        p.IsReadonly,
                        i
                    )).ToList();

                    // Resolve return type
                    var returnType = Model.ResolveType(lambdaFunctionExpression.ReturnType!.Name, scope: codeContainer) ?? throw new BabyPenguinException($"Can't resolve return type '{lambdaFunctionExpression.ReturnType.Name}'", lambdaFunctionExpression.ReturnType.SourceLocation);

                    var closureSymbols = new List<ISymbol>();
                    // Collect non-local symbols from the code block
                    lambdaFunctionExpression.CodeBlock?.TraverseChildren((node, parent) =>
                    {
                        if (node is PrimaryExpression primaryExp && primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                        {
                            // First try to resolve in the lambda's scope
                            var localSymbol = Model.ResolveShortSymbol(primaryExp.Identifier!.Name,
                                 s => s.IsLocal, scopeDepth: primaryExp.ScopeDepth, scope: codeContainer);

                            if (localSymbol != null && localSymbol.ScopeDepth < lambdaFunctionExpression.CodeBlock.ScopeDepth)
                            {
                                closureSymbols.Add(localSymbol);
                            }
                        }
                        return true;
                    });
                    // remove duplicates
                    closureSymbols = closureSymbols.Distinct(new ClosureSymbolEqualityComparer()).ToList();
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Found closure symbol: {string.Join(", ", closureSymbols.Select(i => i.Name))}");

                    // Create the lambda class
                    var lambdaClass = typeContainer.AddLambdaClass(
                        codeContainer.Name,
                        lambdaFunctionExpression.CodeBlock,
                        parameters,
                        returnType,
                        closureSymbols,
                        lambdaFunctionExpression.SourceLocation,
                        lambdaFunctionExpression.ScopeDepth,
                        false, // isStatic
                        lambdaFunctionExpression.ReturnValueIsReadonly ?? false,
                        lambdaFunctionExpression.IsAsync
                    );

                    // Create new expression to instantiate the lambda class
                    var newExp = new ReadMemberAccessExpression();
                    var newArguments = string.Join(", ", closureSymbols.Select(i => i.Name));
                    newExp.FromString($"(new {lambdaClass.FullName}({newArguments})).call", lambdaFunctionExpression.ScopeDepth, Model.Reporter);

                    // Replace the lambda function expression with the new expression
                    if (parent is PrimaryExpression expr)
                    {
                        expr.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                        expr.ParenthesizedExpression = (newExp as ISyntaxExpression).CreateWrapperExpression<Expression>();
                    }
                    else
                    {
                        throw new BabyPenguinException($"Unexpected parent type for lambda function expression: {parent.GetType().Name}", lambdaFunctionExpression.SourceLocation);
                    }

                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Rewrote lambda function to class `{lambdaClass.FullName}`");
                    AddRewritedSource(codeContainer.FullName, Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildSourceText()));
                    AddRewritedSource(lambdaClass.FullName, Tools.FormatPenguinLangSource(lambdaClass.SyntaxNode!.BuildSourceText()));
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
                        if (exp.PrimaryExpression!.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                        {
                            symbol = Model.ResolveShortSymbol(exp.PrimaryExpression.Identifier!.Name,
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
                        else if (parent is PostfixExpression postfixExp)
                        {
                            postfixExp.PostfixExpressionType = PostfixExpression.Type.Wait;
                            postfixExp.SubWaitExpression = node.Build<WaitExpression>(e =>
                            {
                                e.FunctionCallExpression = exp;
                            });
                            postfixExp.SubFunctionCallExpression = null;

                            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Rewrote implicit wait for `{codeContainer.FullName}`");
                            AddRewritedSource(codeContainer.FullName, Tools.FormatPenguinLangSource(codeContainer.SyntaxNode!.BuildSourceText()));
                        }
                        else throw new NotImplementedException();
                    }
                }
                return true;
            });
        }

        public void RewriteAsyncWait(ICodeContainer codeContainer)
        {
            /*
                async fun test() -> bool {}

                initial {
                    var a : bool = wait test();
                    var b : Option<bool> = wait_any test();
                }

                // rewrite to:
                initial {
                    var a : bool = (async test()).wait();
                    var b : Option<bool> = (async test()).wait_any();
                }
            */

            if ((codeContainer is IInitialRoutine) || (codeContainer is IFunction f && f.IsAsync == true))
            {
                codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is WaitExpression waitExp && waitExp.FunctionCallExpression != null)
                    {
                        RewriteAsyncWait(codeContainer, parent, waitExp);
                    }
                    return true;
                });
            }
        }

        private void RewriteAsyncWait(ICodeContainer codecontainer, SyntaxNode parent, FunctionCallExpression functionCallExpression)
        {
            var waitFunctionName = "do_wait";

            var asyncSpawnExp = functionCallExpression.Build<SpawnAsyncExpression>(e =>
            {
                e.Expression = (functionCallExpression as ISyntaxExpression).CreateWrapperExpression<Expression>();
                e.RewritedText = "async " + functionCallExpression.RewritedText;
            });
            var primaryExp = asyncSpawnExp.Build<PrimaryExpression>(e =>
            {
                e.RewritedText = $"({asyncSpawnExp.RewritedText})";
                e.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                e.ParenthesizedExpression = (asyncSpawnExp as ISyntaxExpression).CreateWrapperExpression<Expression>();
            });
            var memberAccessExp = primaryExp.Build<ReadMemberAccessExpression>(e =>
            {
                e.RewritedText = $"{primaryExp.RewritedText}.{waitFunctionName}";
                e.PrimaryExpression = primaryExp;
                e.MemberIdentifiers = [primaryExp.Build<SymbolIdentifier>(e => e.LiteralName = waitFunctionName)];
            });
            var waitFunctionCallExp = memberAccessExp.Build<FunctionCallExpression>(e =>
            {
                e.RewritedText = memberAccessExp.RewritedText + "()";
                e.MemberAccessExpression = memberAccessExp;
            });

            //parent.ReplaceChild(waitExp, functionCallExp);
            if (parent is PostfixExpression postfixExp)
            {
                postfixExp.PostfixExpressionType = PostfixExpression.Type.FunctionCall;
                postfixExp.SubWaitExpression = null;
                postfixExp.SubFunctionCallExpression = waitFunctionCallExp;
            }
            else throw new NotImplementedException();

            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"rewriting wait expression: '{functionCallExpression}' to '{waitFunctionCallExp}'", functionCallExpression.SourceLocation);
            AddRewritedSource(codecontainer.FullName, Tools.FormatPenguinLangSource(codecontainer.SyntaxNode!.BuildSourceText()));
        }

        private void RewriteAsyncWait(ICodeContainer codecontainer, SyntaxNode parent, WaitExpression waitExp)
        {
            if (waitExp.FunctionCallExpression != null)
                RewriteAsyncWait(codecontainer, parent, waitExp.FunctionCallExpression);
            else
                throw new BabyPenguinException($"Can't rewrite empty wait expression: '{waitExp.RewritedText}'", waitExp.SourceLocation);
        }

        public void IdentifyAsyncFunction(IFunction func)
        {
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
                            if (exp.PrimaryExpression!.PrimaryExpressionType == PrimaryExpression.Type.Identifier)
                            {
                                symbol = Model.ResolveShortSymbol(exp.PrimaryExpression.Identifier!.Name,
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
                                    RewritedText = "__yield_not_finished_return " + (yieldStatement.YieldExpression?.RewritedText ?? "") + ";",
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
                    AddRewritedSource(func.FullName, Tools.FormatPenguinLangSource(func.SyntaxNode.BuildSourceText()));
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

                foreach (var kvp in RewritedSource)
                {
                    sb.AppendLine($"Rewrited source for {kvp.Key}:");
                    sb.AppendLine(kvp.Value);
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }
    }
}