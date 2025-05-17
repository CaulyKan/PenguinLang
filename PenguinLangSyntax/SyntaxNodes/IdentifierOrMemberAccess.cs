namespace PenguinLangSyntax.SyntaxNodes
{

    public class IdentifierOrMemberAccess : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is IdentifierOrMemberAccessContext context)
            {
                if (context.identifier() is not null)
                {
                    Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                }
                else if (context.memberAccessExpression() is not null)
                {
                    MemberAccess = Build<WriteMemberAccessExpression>(walker, context.memberAccessExpression());
                }
                else
                {
                    throw new NotImplementedException("Invalid identifier or member access");
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.identifierOrMemberAccess(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? Identifier { get; set; }

        [ChildrenNode]
        public MemberAccessExpression? MemberAccess { get; set; }

        public bool IsIdentifier => Identifier is not null;

        public bool IsMemberAccess => MemberAccess is not null;

        public override string BuildSourceText()
        {
            if (IsIdentifier)
            {
                return Identifier!.BuildSourceText();
            }
            else if (IsMemberAccess)
            {
                return MemberAccess!.BuildSourceText();
            }
            else
            {
                throw new NotImplementedException("Neither identifier nor member access is set");
            }
        }
    }
}