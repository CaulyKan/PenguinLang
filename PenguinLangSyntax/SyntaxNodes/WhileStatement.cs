namespace PenguinLangSyntax.SyntaxNodes
{
    public class WhileStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WhileStatementContext context)
            {
                Condition = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
                BodyStatement = Build<Statement>(walker, context.statement());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.whileStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Condition { get; private set; }

        [ChildrenNode]
        public Statement? BodyStatement { get; private set; }

        public override string BuildText()
        {
            return $"while {Condition!.BuildText()} {BodyStatement!.BuildText()}";
        }
    }
}