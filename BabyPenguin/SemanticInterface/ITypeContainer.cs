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
            result.ScopeDepth = identifier.ScopeDepth;
            result.PrimaryExpression = new PrimaryExpression
            {
                SourceText = "this",
                SourceLocation = identifier.SourceLocation,
                ScopeDepth = identifier.ScopeDepth,
                PrimaryExpressionType = PrimaryExpression.Type.Identifier,
                Identifier = new SymbolIdentifier
                {
                    SourceText = "this",
                    SourceLocation = identifier.SourceLocation,
                    ScopeDepth = identifier.ScopeDepth,
                    LiteralName = "this",
                }
            };
            result.MemberIdentifiers = [ new SymbolIdentifier
                {
                    SourceText = identifier.Name,
                    SourceLocation = identifier.SourceLocation,
                    ScopeDepth = identifier.ScopeDepth,
                    LiteralName = identifier.Name
                }
            ];
            return result;
        }

        public IClassNode AddLambdaClass(string nameHint, SyntaxNode? syntaxNode, List<FunctionParameter> parameters, IType returnType, List<ISymbol> closureSymbols, SourceLocation sourceLocation, uint scopeDepth, bool isPure = false, bool? isAsync = false)
        {
            var parametersString = string.Join(", ", parameters.Select(p => $"{p.Name} : {p.Type.FullName()}"));
            var declarationStrings = closureSymbols.Select(s => $"{s.Name} : {s.TypeInfo.FullName()}").ToList();

            var name = $"__lambda_{nameHint}_{counter++}";
            string text = "";
            if (syntaxNode != null)
            {
                syntaxNode.TraverseChildren((node, parent) =>
                {
                    if (node is IdentifierOrMemberAccess identifierOrMember)
                    {
                        if (identifierOrMember.Identifier != null && closureSymbols.Any(s => s.Name == identifierOrMember.Identifier.Name))
                        {
                            identifierOrMember.MemberAccess = CreateMemberAccess(false, identifierOrMember.Identifier);
                            identifierOrMember.Identifier = null;
                        }
                    }
                    else if (node is PrimaryExpression primary)
                    {
                        if (primary.PrimaryExpressionType == PrimaryExpression.Type.Identifier &&
                            primary.Identifier != null &&
                            closureSymbols.Any(s => s.Name == primary.Identifier.Name))
                        {
                            primary.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                            primary.ParenthesizedExpression = CreateMemberAccess(true, primary.Identifier);
                        }
                    }
                    return true;
                });
                text = syntaxNode.BuildText();
            }

            var source = @$"
                class {name} {{
                    {string.Join("\n", declarationStrings.Select(i => i + ";"))}
                    fun new(this: mut {name}{(declarationStrings.Count > 0 ? ", " : "")}{string.Join(", ", declarationStrings)}) {{
                        {string.Join("\n", closureSymbols.Select(s => $"this.{s.Name} = {s.Name};"))}
                    }}
                    fun call(this: mut {name}{(!string.IsNullOrEmpty(parametersString) ? ", " : "")}{parametersString}) -> {returnType.FullName()} {{
                        {text}
                    }}
                }}
            ";

            var classDefinition = new ClassDefinition();
            classDefinition.FromString(source, (this.SyntaxNode?.ScopeDepth ?? 0) + 1, Reporter);
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