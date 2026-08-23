namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// `connect(source, sink);` — insert a fresh wire (latest-wins channel)
    /// between an output port (or channel) and an input port (or channel).
    /// Legal only inside a construct block.
    /// </summary>
    public class ConnectStatement : SyntaxNode
    {
        [ChildrenNode]
        public ISyntaxExpression? Source { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? Sink { get; set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ConnectStatementContext context)
            {
                var exprs = context.expression();
                if (exprs.Length == 2)
                {
                    Source = Build<Expression>(walker, exprs[0]).GetEffectiveExpression();
                    Sink = Build<Expression>(walker, exprs[1]).GetEffectiveExpression();
                }
                else throw new NotImplementedException("connect expects exactly two expressions");
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.connectStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public override string ToShortString() => "connect";

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("connect");
            parts.Add("(" + Source!.BuildText() + ", " + Sink!.BuildText() + ");");
            return string.Join(" ", parts);
        }
    }
}
