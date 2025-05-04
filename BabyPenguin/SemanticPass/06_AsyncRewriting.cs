
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
        }

        public void Process()
        {
            foreach (var func in Model.FindAll(i => i is IFunction))
            {
                Process(func);
            }
        }

        public void RewriteAsyncWait(ICodeContainer codeContainer)
        {
            /*
                async fun test() -> bool {}

                initial {
                    var a : bool = wait test();
                }

                // rewrite to:
                initial {
                    var a : bool;
                    var f: IFuture<bool> = async test();
                    while (true) {
                        wait;
                        var res : FutureState<bool> = f.poll(); 
                        if (res is FutureState<bool>.ready_finished) {
                            a = res.ready_finished;
                            break;
                        } 
                        else if (res is FutureState<bool>.ready) {
                            a = res.ready;
                        } 
                        else if (res is FutureState<bool>.finished) {
                            break;
                        }
                    }
                }
            */

            codeContainer.CodeSyntaxNode?.TraverseChildren((node, parent) =>
            {
                if (node is WaitExpression waitExp && waitExp.Expression != null)
                {

                }
                return true;
            });
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
                                    s => !s.IsClassMember, scopeDepth: exp.Scope.ScopeDepth, scope: func);
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