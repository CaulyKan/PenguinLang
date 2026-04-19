namespace PenguinLangParser.SyntaxNodes
{

    public abstract class MemberAccessExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                BaseExpression = Build<PostfixExpression>(walker, context.postfixExpression()).GetEffectiveExpression();
                Member = Build<SymbolIdentifier>(walker, context.identifierWithGeneric());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? BaseExpression { get; set; }

        [ChildrenNode]
        public Identifier? Member { get; set; }

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string ToShortString() => "";

        public override string BuildText()
        {
            return $"{BaseExpression!.BuildText()}.{Member!.BuildText()}";
        }

        public abstract bool IsWrite { get; }
    }

    public class ReadMemberAccessExpression : MemberAccessExpression
    {
        public override bool IsWrite => false;
    }

    public class WriteMemberAccessExpression : MemberAccessExpression
    {
        [SexpValue]
        public override bool IsWrite => true;
    }

}
