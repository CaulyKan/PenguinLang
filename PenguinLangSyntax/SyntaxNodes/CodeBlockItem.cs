namespace PenguinLangSyntax.SyntaxNodes
{

    public class CodeBlockItem : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is CodeBlockItemContext context)
            {
                if (context.statement() is not null)
                {
                    Statement = Build<Statement>(walker, context.statement());
                }
                else if (context.declaration() is not null)
                {
                    Declaration = Build<Declaration>(walker, context.declaration());
                }
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Statement? Statement { get; private set; }

        [ChildrenNode]
        public Declaration? Declaration { get; private set; }

        public bool IsDeclaration => Declaration is not null;
    }

}