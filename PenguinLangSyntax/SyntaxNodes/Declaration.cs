namespace PenguinLangSyntax.SyntaxNodes
{

    public class Declaration : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is DeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                if (context.typeSpecifier() != null)
                {
                    TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                    walker.DefineSymbol(Name, TypeSpecifier.Name, this);
                }
                else
                {
                    throw new NotImplementedException("Type infer is not supported yet");
                }
                IsReadonly = context.declarationKeyword().GetText() == "val";

                if (context.expression() != null)
                    InitializeExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
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

        public bool IsReadonly;

        public string Name => Identifier!.Name;

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add(IsReadonly ? "val" : "var");
            parts.Add(Identifier!.BuildSourceText());
            parts.Add(":");
            parts.Add(TypeSpecifier!.BuildSourceText());
            if (InitializeExpression != null)
            {
                parts.Add("=");
                parts.Add(InitializeExpression.BuildSourceText());
            }
            return string.Join(" ", parts);
        }
    }
}