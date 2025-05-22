namespace PenguinLangSyntax.SyntaxNodes
{

    public class WhereClause : SyntaxNode
    {
        [ChildrenNode]
        public Identifier? Identifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; private set; }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.whereClause(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WhereClauseContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
            }
            else throw new NotImplementedException();
        }

        public override string BuildText()
        {
            return $"{Identifier!.BuildText()}: {TypeSpecifier!.BuildText()}";
        }
    }
}