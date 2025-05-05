namespace PenguinLangSyntax.SyntaxNodes
{

    public class TypeSpecifier : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TypeSpecifierContext context)
            {
                Name = context.GetText();
            }
            else throw new NotImplementedException();
        }

        public string Name { get; set; } = "";
    }
}