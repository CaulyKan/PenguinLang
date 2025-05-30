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
                    PrimaryExpression = Build<PrimaryExpression>(walker, context.primaryExpression()).GetEffectiveExpression();
                }
                else if (context.memberAccessExpression() != null)
                {
                    MemberAccessExpression = Build<ReadMemberAccessExpression>(walker, context.memberAccessExpression());
                }
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x).GetEffectiveExpression())
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.functionCallExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;

        [ChildrenNode]
        public ISyntaxExpression? PrimaryExpression { get; set; }

        [ChildrenNode]
        public MemberAccessExpression? MemberAccessExpression { get; set; }

        public bool IsMemberAccess => MemberAccessExpression is not null;

        [ChildrenNode]
        public List<ISyntaxExpression> ArgumentsExpression { get; private set; } = [];

        public bool IsSimple => false;

        public override string BuildText()
        {
            var parts = new List<string>();
            if (IsMemberAccess)
            {
                parts.Add(MemberAccessExpression!.BuildText());
            }
            else
            {
                parts.Add(PrimaryExpression!.BuildText());
            }

            parts.Add("(");
            if (ArgumentsExpression.Count > 0)
            {
                parts.Add(string.Join(", ", ArgumentsExpression.Select(e => e.BuildText())));
            }
            parts.Add(")");

            return string.Join("", parts);
        }
    }
}