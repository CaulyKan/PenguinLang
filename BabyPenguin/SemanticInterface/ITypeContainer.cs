using System.Threading;

namespace BabyPenguin.SemanticInterface
{

    public interface ITypeContainer : ISemanticScope
    {
        void AddClass(ClassNode cls)
        {
            Classes.Add(cls);
            cls.Parent = this;
        }

        void AddEnum(SemanticNode.EnumNode enm)
        {
            Enums.Add(enm);
            enm.Parent = this;
        }

        void AddInterface(InterfaceNode intf)
        {
            Interfaces.Add(intf);
            intf.Parent = this;
        }

        static ulong counter = 0;

        MemberAccessExpression CreateMemberAccess(bool isRead, Identifier identifier)
        {
            MemberAccessExpression result = isRead ? new ReadMemberAccessExpression() : new WriteMemberAccessExpression();
            result.SourceText = $"this.{identifier.Name}";
            result.SourceLocation = identifier.SourceLocation;
            result.ScopeId = identifier.ScopeId;
            result.BaseExpression = new PrimaryExpression
            {
                SourceText = "this",
                SourceLocation = identifier.SourceLocation,
                ScopeId = identifier.ScopeId,
                PrimaryExpressionType = PrimaryExpression.Type.Identifier,
                Identifier = new SymbolIdentifier
                {
                    SourceText = "this",
                    SourceLocation = identifier.SourceLocation,
                    ScopeId = identifier.ScopeId,
                    LiteralName = "this",
                }
            };
            result.Member = new SymbolIdentifier
            {
                SourceText = identifier.Name,
                SourceLocation = identifier.SourceLocation,
                ScopeId = identifier.ScopeId,
                LiteralName = identifier.Name
            };
            return result;
        }

        public IClassNode AddLambdaClass(string nameHint, SyntaxNode? syntaxNode, List<FunctionParameter> parameters, IType returnType, List<ISymbol> closureSymbols, SourceLocation sourceLocation, bool isPure = false, bool? isAsync = false, List<FunctionParameter>? captureParameters = null)
        {
            var parametersString = string.Join(", ", parameters.Select(p => $"{p.Name} : {p.Type.FullName()}"));
            // Capture PARAMETERS (used by the generator desugar): unlike
            // `parameters` (which feed the `call` signature), these are baked
            // into the closure at construction as snapshot fields — the body's
            // references to them are rewritten to `this.<name>` exactly like
            // closure symbols.
            var captureParams = (captureParameters ?? []).Where(p => p.Name != "this").ToList();
            var declarationStrings = closureSymbols.Select(s => $"{s.Name} : {s.TypeInfo.FullName()}").ToList();
            declarationStrings.AddRange(captureParams.Select(p => $"{p.Name} : {p.Type.FullName()}"));
            var capturedNames = new HashSet<string>(closureSymbols.Select(s => s.Name).Concat(captureParams.Select(p => p.Name)));
            var ctorArgNames = string.Join(", ", closureSymbols.Select(s => s.Name).Concat(captureParams.Select(p => p.Name)));

            var name = $"__lambda_{nameHint}_{Interlocked.Increment(ref counter)}";
            string text = "";
            if (syntaxNode != null)
            {
                syntaxNode.TraverseChildren((node, parent) =>
                {
                    if (node is PostfixExpression postfix)
                    {
                        if (postfix.PostfixExpressionType == PostfixExpression.Type.PrimaryExpression &&
                            postfix.SubPrimaryExpression != null &&
                            postfix.SubPrimaryExpression.PrimaryExpressionType == PrimaryExpression.Type.Identifier &&
                            postfix.SubPrimaryExpression.Identifier != null &&
                            capturedNames.Contains(postfix.SubPrimaryExpression.Identifier.Name))
                        {
                            var memberAccess = CreateMemberAccess(true, postfix.SubPrimaryExpression.Identifier);
                            postfix.PostfixExpressionType = PostfixExpression.Type.MemberAccess;
                            postfix.SubPrimaryExpression = null;
                            postfix.SubMemberAccessExpression = memberAccess;
                        }
                    }
                    else if (node is PrimaryExpression primaryExp &&
                        primaryExp.PrimaryExpressionType == PrimaryExpression.Type.Identifier &&
                        primaryExp.Identifier != null &&
                        capturedNames.Contains(primaryExp.Identifier.Name))
                    {
                        // PrimaryExpression was unwrapped from PostfixExpression via GetEffectiveExpression
                        // Convert it to a ParenthesizedExpression wrapping the member access
                        var memberAccess = CreateMemberAccess(true, primaryExp.Identifier);
                        var wrapper = new PostfixExpression
                        {
                            PostfixExpressionType = PostfixExpression.Type.MemberAccess,
                            SubMemberAccessExpression = memberAccess,
                            SourceLocation = primaryExp.SourceLocation,
                            ScopeId = primaryExp.ScopeId,
                        };
                        primaryExp.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                        primaryExp.ParenthesizedExpression = wrapper;
                        primaryExp.Identifier = null;
                    }
                    return true;
                });
                text = syntaxNode.BuildText();
            }

            var source = @$"
                class {name} {{
                    {string.Join("\n", declarationStrings.Select(i => i + ";"))}
                    fun new(this: mut {name}{(declarationStrings.Count > 0 ? ", " : "")}{string.Join(", ", declarationStrings)}) {{
                        {string.Join("\n", capturedNames.Select(s => $"this.{s} = {s};"))}
                    }}
                    fun call(this: mut {name}{(!string.IsNullOrEmpty(parametersString) ? ", " : "")}{parametersString}) -> {returnType.FullName()} {{
                        {text}
                    }}
                }}
            ";

            var classDefinition = new ClassDefinition();
            classDefinition.FromString(source, Reporter);
            var cls = new ClassNode(Model, classDefinition);

            AddClass(cls);

            Model.Reporter.Write(DiagnosticLevel.Debug, $"Adding lambda class/function {cls.Name}");
            Model.CatchUp(cls);

            return cls;
        }

        List<ClassNode> Classes { get; }

        List<SemanticNode.EnumNode> Enums { get; }

        List<InterfaceNode> Interfaces { get; }
    }

}