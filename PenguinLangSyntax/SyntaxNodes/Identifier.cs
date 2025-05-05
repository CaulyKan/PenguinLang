namespace PenguinLangSyntax.SyntaxNodes
{

    public abstract class Identifier : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is IdentifierContext context)
            {
                this.LiteralName = context.GetText();
            }
            else if (ctx is TypeSpecifierContext context2)
            {
                this.LiteralName = context2.GetText();
            }
            else if (ctx is IdentifierWithGenericContext context3)
            {
                this.LiteralName = context3.GetText();
            }
            else throw new NotImplementedException();
        }

        public abstract bool IsType { get; }

        public Identifier? Parent { get; set; }

        public string LiteralName { get; set; } = "";

        public string Name => LiteralName;
    }

    public class TypeIdentifier : Identifier
    {
        override public bool IsType => true;
    }

    public class SymbolIdentifier : Identifier
    {
        override public bool IsType => false;
    }
}