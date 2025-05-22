namespace PenguinLangSyntax.SyntaxNodes
{

    public abstract class MemberAccessExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is MemberAccessExpressionContext context)
            {
                PrimaryExpression = Build<PrimaryExpression>(walker, context.primaryExpression()).GetEffectiveExpression();
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
        public ISyntaxExpression? PrimaryExpression { get; set; }

        [ChildrenNode]
        public List<Identifier> MemberIdentifiers { get; set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add(PrimaryExpression!.BuildText());

            foreach (var member in MemberIdentifiers)
            {
                parts.Add(".");
                parts.Add(member.BuildText());
            }

            return string.Join("", parts);
        }

        public abstract bool IsWrite { get; }
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