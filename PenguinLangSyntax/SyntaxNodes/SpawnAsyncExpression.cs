namespace PenguinLangSyntax.SyntaxNodes
{

    public class SpawnAsyncExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is SpawnExpressionContext context)
            {
                Expression = Build<Expression>(walker, context.expression());
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public Expression? Expression { get; set; }

        public bool IsSimple => false;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubSpawnAsyncExpression = this,
                PostfixExpressionType = PostfixExpression.Type.SpawnAsync
            };
        }
    }
}