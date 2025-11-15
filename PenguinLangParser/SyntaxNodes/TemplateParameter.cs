namespace PenguinLangParser.SyntaxNodes
{

    public class TemplateParameter : SyntaxNode
    {
        [ChildrenNode]
        public Identifier? Name { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? ParameterType { get; private set; }

        [SexpValue]
        public bool IsTypeParameter { get; private set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TemplateParameterContext context)
            {
                Name = Build<SymbolIdentifier>(walker, context.identifier());

                var typeCtx = context.children.OfType<TypeSpecifierContext>().FirstOrDefault();
                if (typeCtx != null)
                {
                    ParameterType = Build<TypeSpecifier>(walker, typeCtx);
                    IsTypeParameter = false;
                }
                else
                {
                    IsTypeParameter = true;
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.templateParameter(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string ToShortString() => "";

        public override string BuildText()
        {
            var typeText = IsTypeParameter ? "type" : ParameterType!.BuildText();
            return $"{Name!.BuildText()} : {typeText}";
        }
    }
}

