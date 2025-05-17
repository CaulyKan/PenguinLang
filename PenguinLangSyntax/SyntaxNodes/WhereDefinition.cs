namespace PenguinLangSyntax.SyntaxNodes
{

    public class WhereDefinition : SyntaxNode
    {
        [ChildrenNode]
        public List<WhereClause> WhereClauses { get; private set; } = [];

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WhereDefinitionContext context)
            {
                WhereClauses = context.children.OfType<WhereClauseContext>()
                   .Select(x => Build<WhereClause>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.whereDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override string BuildSourceText()
        {
            if (WhereClauses.Count == 0)
            {
                return "";
            }

            return $"where {string.Join(", ", WhereClauses.Select(c => c.BuildSourceText()))}";
        }
    }
}