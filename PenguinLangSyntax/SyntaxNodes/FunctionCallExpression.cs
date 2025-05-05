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

        [ChildrenNode]
        public PrimaryExpression? PrimaryExpression { get; private set; }

        [ChildrenNode]
        public MemberAccessExpression? MemberAccessExpression { get; private set; }

        public bool IsMemberAccess => MemberAccessExpression is not null;

        [ChildrenNode]
        public List<Expression> ArgumentsExpression { get; private set; } = [];

        public bool IsSimple => false;
    }
}