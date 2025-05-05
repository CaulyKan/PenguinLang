namespace PenguinLangSyntax.SyntaxNodes
{

    public abstract class MemberAccessExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is MemberAccessExpressionContext context)
            {
                PrimaryExpression = Build<PrimaryExpression>(walker, context.primaryExpression());
                MemberIdentifiers = context.children.OfType<IdentifierWithGenericContext>()
                   .Select(x => Build<SymbolIdentifier>(walker, x) as Identifier)
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        [ChildrenNode]
        public PrimaryExpression? PrimaryExpression { get; set; }

        [ChildrenNode]
        public List<Identifier> MemberIdentifiers { get; set; } = [];

        public bool IsSimple => false;

        public abstract bool IsWrite { get; }

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubMemberAccessExpression = this,
                PostfixExpressionType = PostfixExpression.Type.MemberAccess
            };
        }
    }

    public class ReadMemberAccessExpression : MemberAccessExpression
    {
        public override bool IsWrite => false;
    }

    public class WriteMemberAccessExpression : MemberAccessExpression
    {
        public override bool IsWrite => true;
    }

}