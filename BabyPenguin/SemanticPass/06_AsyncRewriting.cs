
using System.Security.Cryptography;

namespace BabyPenguin.SemanticPass
{


    public class AsyncRewritingPass(SemanticModel model, int passIndex) : ISemanticPass
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
                RewriteAsyncWait(codeContainer);
                RewriteImplicitWait(codeContainer);
            }
        }

        public void Process()
        {
            foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
            {
                IdentifyAsyncFunction(func);
            }

            foreach (var func in Model.FindAll(i => i is ICodeContainer).Cast<ICodeContainer>())
            {
                if (func is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is IType t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Async rewriting pass for '{func.FullName}' is skipped now because it is inside a generic type");
                }
                else
                {
                    RewriteImplicitWait(func);
                    RewriteAsyncWait(func);
                }
            }
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
                        if (codeContainer.CheckMemberAccessExpressionIsStatic(exp.MemberAccessExpression!))
                        {
                            symbol = Model.ResolveSymbol(exp.MemberAccessExpression!.Text, s => s.IsStatic, scope: codeContainer);
                        }
                        else
                        {
                            codeContainer.ResolveMemberAccessExpressionSymbol(exp.MemberAccessExpression!, out _, out symbol);
                        }
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

                    if (symbol == null || !symbol.IsFunction) Model.Reporter.Throw($"Can't resolve function symbol {exp}", exp.SourceLocation);

                    var callingFunc = (symbol as FunctionSymbol)?.CodeContainer as IFunction;
                    if (callingFunc == null) Model.Reporter.Throw($"Can't resolve function symbol context {exp}", exp.SourceLocation);

                    if (callingFunc.IsAsync == true)
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
                                e.IsWaitAny = false;
                                e.FunctionCallExpression = exp;
                            });
                            postfixExp.SubFunctionCallExpression = null;
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

            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is WaitExpression waitExp && waitExp.FunctionCallExpression != null)
                {
                    RewriteAsyncWait(parent, waitExp);
                }
                return true;
            });
        }

        private void RewriteAsyncWait(SyntaxNode parent, FunctionCallExpression functionCallExpression, bool isWaitAny)
        {
            var waitFunctionName = isWaitAny ? "do_wait_any" : "do_wait";

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
        }


        private void RewriteAsyncWait(SyntaxNode parent, WaitExpression waitExp)
        {
            if (waitExp.FunctionCallExpression != null)
                RewriteAsyncWait(parent, waitExp.FunctionCallExpression, waitExp.IsWaitAny);
            else
                Model.Reporter.Throw($"Can't rewrite empty wait expression: '{waitExp.RewritedText}'", waitExp.SourceLocation);
        }


        public void IdentifyAsyncFunction(IFunction func)
        {
            if (func.IsAsync != null) return;  // declared as async/!async, respect

            func.IsAsync = false;
            func.SyntaxNode?.TraverseChildren((node, parent) =>
                {
                    if (node is YieldStatement)
                    {
                        func.IsAsync = true;
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Mark function {func.FullName} as async because it has yield statement", node.SourceLocation);
                        return false;
                    }
                    else if (node is WaitExpression)
                    {
                        func.IsAsync = true;
                        Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Mark function {func.FullName} as async because it has wait statement", node.SourceLocation);
                        return false;
                    }
                    else if (node is FunctionCallExpression exp)
                    {
                        ISymbol? symbol;
                        if (exp.IsMemberAccess)
                        {
                            if (func.CheckMemberAccessExpressionIsStatic(exp.MemberAccessExpression!))
                            {
                                symbol = Model.ResolveSymbol(exp.MemberAccessExpression!.Text, s => s.IsStatic, scope: func);
                            }
                            else
                            {
                                func.ResolveMemberAccessExpressionSymbol(exp.MemberAccessExpression!, out _, out symbol);
                            }
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

                        if (symbol == null || !symbol.IsFunction) Model.Reporter.Throw($"Can't resolve function symbol {exp}", exp.SourceLocation);

                        var callingFunc = (symbol as FunctionSymbol)?.CodeContainer as IFunction;
                        if (callingFunc == null) Model.Reporter.Throw($"Can't resolve function symbol context {exp}", exp.SourceLocation);

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
                    return true;
                });
        }

        public string Report
        {
            get
            {
                var table = new ConsoleTable("Function", "IsAsync");
                foreach (var func in Model.FindAll(i => i is IFunction).Cast<IFunction>())
                {
                    table.AddRow(func.FullName, func.IsAsync);
                }
                return table.ToMarkDownString();
            }
        }
    }
}