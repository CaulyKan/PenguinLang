namespace PenguinLangSyntax.SyntaxNodes
{

    public class FunctionCallExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is FunctionCallExpressionContext context)
            {
                if (context.primaryExpression() != null)
                {
                    PrimaryExpression = Build<PrimaryExpression>(walker, context.primaryExpression());
                }
                else if (context.memberAccessExpression() != null)
                {
                    MemberAccessExpression = Build<ReadMemberAccessExpression>(walker, context.memberAccessExpression());
                }
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.functionCallExpression(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;

        [ChildrenNode]
        public PrimaryExpression? PrimaryExpression { get; set; }

        [ChildrenNode]
        public MemberAccessExpression? MemberAccessExpression { get; set; }

        public bool IsMemberAccess => MemberAccessExpression is not null;

        [ChildrenNode]
        public List<Expression> ArgumentsExpression { get; private set; } = [];

        public bool IsSimple => false;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubFunctionCallExpression = this,
                PostfixExpressionType = PostfixExpression.Type.FunctionCall
            };
        }
    }
}