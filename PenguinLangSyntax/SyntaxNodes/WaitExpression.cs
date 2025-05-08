namespace PenguinLangSyntax.SyntaxNodes
{

    public class WaitExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WaitExpressionContext context)
            {
                FunctionCallExpression = context.functionCallExpression() != null ? Build<FunctionCallExpression>(walker, context.functionCallExpression()) : null;
                IsWaitAny = context.GetText().StartsWith("wait_any");
            }
            else throw new NotImplementedException();
        }

        public bool IsWaitAny { get; set; } = false;

        [ChildrenNode]
        public FunctionCallExpression? FunctionCallExpression { get; set; }

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