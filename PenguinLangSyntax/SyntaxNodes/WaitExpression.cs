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
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.waitExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public FunctionCallExpression? FunctionCallExpression { get; set; }

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

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

        public override string BuildSourceText()
        {
            if (FunctionCallExpression == null)
                return "wait;";
            else
                return $"wait {FunctionCallExpression!.BuildSourceText()}";
        }
    }
}