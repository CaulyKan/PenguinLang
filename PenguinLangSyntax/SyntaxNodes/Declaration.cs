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
                    InitializeExpression = Build<Expression>(walker, context.expression());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.declaration(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? Identifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        [ChildrenNode]
        public Expression? InitializeExpression { get; private set; }

        public bool IsReadonly;

        public string Name => Identifier!.Name;
    }
}