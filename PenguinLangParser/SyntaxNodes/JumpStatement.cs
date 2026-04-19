namespace PenguinLangParser.SyntaxNodes
{

    public class JumpStatement : SyntaxNode
    {
        public enum Type
        {
            Break,
            Continue
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is JumpStatementContext context)
            {
                var text = context.GetText();
                if (text.StartsWith("break"))
                {
                    JumpType = Type.Break;
                    // Parse optional break expression
                    if (context.expression() != null)
                    {
                        BreakExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
                    }
                }
                else if (text.StartsWith("continue"))
                {
                    JumpType = Type.Continue;
                }
                else
                {
                    throw new NotImplementedException("Invalid jump statement type");
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.jumpStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [SexpValue]
        public Type JumpType { get; private set; }

        [ChildrenNode]
        public ISyntaxExpression? BreakExpression { get; private set; }

        [SexpValue]
        public bool HasBreakExpression => BreakExpression != null;

        public override string ToShortString() => JumpType.ToString();

        public override string BuildText()
        {
            return JumpType switch
            {
                Type.Break => BreakExpression != null ? $"break {BreakExpression.BuildText()};" : "break;",
                Type.Continue => "continue;",
                _ => throw new NotImplementedException($"Unsupported JumpType: {JumpType}")
            };
        }
    }

}