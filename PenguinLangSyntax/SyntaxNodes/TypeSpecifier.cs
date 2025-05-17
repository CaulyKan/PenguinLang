namespace PenguinLangSyntax.SyntaxNodes
{

    public class TypeSpecifier : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TypeSpecifierContext context)
            {
                TypeName = context.typeSpecifierWithoutIterable().GetText();
                IsIterable = context.iterableType() != null;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.typeSpecifier(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public string TypeName { get; set; } = "";

        public string Name => IsIterable ? $"__builtin.IIterator<{TypeName}>" : TypeName;

        public bool IsIterable { get; set; } = false;

        public override string BuildSourceText()
        {
            return IsIterable ? $"__builtin.IIterator<{TypeName}>" : TypeName;
        }
    }
}