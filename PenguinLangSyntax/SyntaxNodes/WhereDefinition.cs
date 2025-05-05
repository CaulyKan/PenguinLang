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
    }
}