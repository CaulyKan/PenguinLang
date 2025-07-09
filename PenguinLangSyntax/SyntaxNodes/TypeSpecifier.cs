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
                IsMutable = context.typeMutabilitySpecifier() != null;
                IsIterable = context.iterableType() != null;

                if (IsMutable)
                    TypeName = "mut " + TypeName;
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.typeSpecifier(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public bool IsMutable { get; set; } = false;

        public string TypeName { get; set; } = "";

        public string Name => IsIterable ? $"__builtin.IIterator<{TypeName}>" : TypeName;

        public bool IsIterable { get; set; } = false;

        public override string BuildText()
        {
            return IsIterable ? $"__builtin.IIterator<{TypeName}>" : TypeName;
        }
    }
}