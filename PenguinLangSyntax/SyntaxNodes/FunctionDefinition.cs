namespace PenguinLangSyntax.SyntaxNodes
{

    public class FunctionDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is FunctionDefinitionContext context)
            {
                walker.PushScope(SyntaxScopeType.Function, this);

                if (context.identifier() != null)
                    FunctionIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                else
                    FunctionIdentifier = new SymbolIdentifier
                    {
                        LiteralName = "new"
                    };

                if (context.parameterList()?.children == null)
                {
                    Parameters = [];
                }
                else
                {
                    Parameters = [];
                    if (context.parameterList().thisParameter() != null)
                    {
                        var isMutable = context.parameterList().thisParameter().GetText() != "this";
                        var text = isMutable ? "mut this" : "this";
                        var sourceLocation = SourceLocation.From(walker.FileName, context.parameterList().thisParameter());
                        Parameters.Add(new Declaration
                        {
                            Identifier = new SymbolIdentifier { LiteralName = "this", ScopeDepth = ScopeDepth, SourceLocation = sourceLocation, SourceText = text },
                            ScopeDepth = ScopeDepth,
                            SourceLocation = sourceLocation,
                            SourceText = text,
                            TypeSpecifier = new TypeSpecifier { IsIterable = false, IsMutable = isMutable ? Mutability.Mutable : Mutability.Immutable, ScopeDepth = ScopeDepth, SourceLocation = sourceLocation, SourceText = text, TypeName = isMutable ? "mut Self" : "Self" }
                        });
                    }
                    Parameters.AddRange(context.parameterList().children.OfType<DeclarationContext>()
                        .Select(x => Build<Declaration>(walker, x)));
                }

                if (context.typeSpecifier() == null)
                {
                    ReturnType = new TypeSpecifier
                    {
                        TypeName = "void",
                        IsIterable = false
                    };
                }
                else
                {
                    ReturnType = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }

                IsExtern = false;
                IsAsync = null;
                IsPure = null;

                foreach (var specifierContext in context.children.OfType<FunctionSpecifierContext>())
                {
                    if (specifierContext.GetText() == "extern")
                    {
                        IsExtern = true;
                    }
                    else if (specifierContext.GetText() == "pure")
                    {
                        IsPure = true;
                    }
                    else if (specifierContext.GetText() == "!pure")
                    {
                        IsPure = false;
                    }
                    else if (specifierContext.GetText() == "async")
                    {
                        IsAsync = true;
                    }
                    else if (specifierContext.GetText() == "!async")
                    {
                        IsAsync = false;
                    }
                }

                if (context.codeBlock() != null)
                    CodeBlock = Build<CodeBlock>(walker, context.codeBlock());

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.functionDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? FunctionIdentifier { get; set; }

        [ChildrenNode]
        public List<Declaration> Parameters { get; set; } = [];

        [ChildrenNode]
        public TypeSpecifier? ReturnType { get; set; }

        [ChildrenNode]
        public CodeBlock? CodeBlock { get; set; }

        public string Name => FunctionIdentifier!.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Function;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous => false;

        public bool IsExtern { get; set; }

        public bool? IsPure { get; set; }

        public bool? IsAsync { get; set; }

        public override string BuildText()
        {
            var parts = new List<string>();

            if (IsExtern)
            {
                parts.Add("extern");
            }

            if (IsPure == true)
            {
                parts.Add("pure");
            }
            else if (IsPure == false)
            {
                parts.Add("!pure");
            }

            if (IsAsync == true)
            {
                parts.Add("async");
            }
            else if (IsAsync == false)
            {
                parts.Add("!async");
            }

            parts.Add("fun");
            parts.Add(FunctionIdentifier!.BuildText());
            parts.Add("(");
            if (Parameters.Count > 0)
            {
                parts.Add(string.Join(", ", Parameters.Select(p => p.BuildText())));
            }
            parts.Add(")");

            if (ReturnType != null)
            {
                parts.Add("->");
                parts.Add(ReturnType.BuildText());
            }
            if (CodeBlock != null)
            {
                parts.Add(CodeBlock.BuildText());
            }

            return string.Join(" ", parts);
        }
    }
}