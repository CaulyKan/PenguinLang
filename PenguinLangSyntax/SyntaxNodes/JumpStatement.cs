namespace PenguinLangSyntax.SyntaxNodes
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
                if (context.jumpKeyword().GetText() == "break")
                {
                    JumpType = Type.Break;
                }
                else if (context.jumpKeyword().GetText() == "continue")
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

        public Type JumpType { get; private set; }
    }

}