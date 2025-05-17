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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.memberAccessExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public PrimaryExpression? PrimaryExpression { get; set; }

        [ChildrenNode]
        public List<Identifier> MemberIdentifiers { get; set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string BuildSourceText()
        {
            var parts = new List<string>();
            parts.Add(PrimaryExpression!.BuildSourceText());

            foreach (var member in MemberIdentifiers)
            {
                parts.Add(".");
                parts.Add(member.BuildSourceText());
            }

            return string.Join("", parts);
        }

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