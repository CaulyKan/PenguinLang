namespace PenguinLangSyntax.SyntaxNodes
{

    public class WaitExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WaitExpressionContext context)
            {
                Expression = context.expression() != null ? Build<Expression>(walker, context.expression()) : null;
                IsWaitAny = context.GetText().StartsWith("wait_any");
            }
            else throw new NotImplementedException();
        }

        public bool IsWaitAny { get; private set; } = false;

        [ChildrenNode]
        public Expression? Expression { get; private set; }

        public bool IsSimple => false;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubWaitExpression = this,
                PostfixExpressionType = PostfixExpression.Type.Wait
            };
        }
    }
}