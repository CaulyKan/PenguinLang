namespace PenguinLangParser.SyntaxNodes
{

    public class Declaration : SyntaxNode
    {
        [SexpValue]
        public bool SuggestMutableTypeInfer { get; private set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is DeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());

                if (context.Parent is CodeBlockItemContext codeBlockItem)
                {
                    if (codeBlockItem.letKeyword().GetText().EndsWith("mut"))
                    {
                        if (context.typeSpecifier() != null)
                            throw new PenguinLangException("Cannot use 'let mut' with an explicit type specifier. Use 'let' instead.", SourceLocation.ToString());

                        SuggestMutableTypeInfer = true;
                    }
                }
                else if (context.Parent is NamespaceDeclarationContext namespaceDeclaration)
                {
                    if (namespaceDeclaration.letKeyword().GetText().EndsWith("mut"))
                    {
                        if (context.typeSpecifier() != null)
                            throw new PenguinLangException("Cannot use 'let mut' with an explicit type specifier. Use 'let' instead.", SourceLocation.ToString());

                        SuggestMutableTypeInfer = true;
                    }
                }
                else if (context.Parent is ForStatementContext forStatement)
                {
                    if (forStatement.letKeyword().GetText().EndsWith("mut"))
                    {
                        if (context.typeSpecifier() != null)
                            throw new PenguinLangException("Cannot use 'let mut' with an explicit type specifier. Use 'let' instead.", SourceLocation.ToString());

                        SuggestMutableTypeInfer = true;
                    }
                }

                if (context.typeSpecifier() != null)
                {
                    TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }
                else
                {
                    // Type inference will be handled in a later pass!
                }

                if (context.expression() != null)
                    InitializeExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else if (ctx is DeclarationWithoutInitializerContext context2)
            {
                Identifier = Build<SymbolIdentifier>(walker, context2.identifier());
                if (context2.typeSpecifier() != null)
                {
                    TypeSpecifier = Build<TypeSpecifier>(walker, context2.typeSpecifier());
                }
                else
                {
                    // Type inference will be handled in a later pass!
                }
            }
            else if (ctx is ClassDeclarationContext)
            {
                // do nothing
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.declaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? Identifier { get; set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? InitializeExpression { get; set; }

        public string Name => Identifier!.Name;

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add(Identifier!.BuildText());
            if (TypeSpecifier != null)
            {
                parts.Add(":");
                parts.Add(TypeSpecifier.BuildText());
            }
            if (InitializeExpression != null)
            {
                parts.Add("=");
                parts.Add(InitializeExpression.BuildText());
            }
            return string.Join(" ", parts);
        }
    }
}