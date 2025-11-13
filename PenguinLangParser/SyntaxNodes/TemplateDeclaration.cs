namespace PenguinLangParser.SyntaxNodes
{

    public class TemplateDeclaration : SyntaxNode
    {
        [ChildrenNode]
        public List<TemplateParameter> Parameters { get; private set; } = [];

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TemplateDeclarationContext context)
            {
                Parameters = context.children.OfType<TemplateParameterContext>()
                   .Select(x => Build<TemplateParameter>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.templateDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildText()
        {
            if (Parameters.Count == 0)
            {
                return "";
            }

            return $"#template({string.Join(", ", Parameters.Select(p => p.BuildText()))})";
        }
    }
}

