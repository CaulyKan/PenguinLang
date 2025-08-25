namespace PenguinLangSyntax.SyntaxNodes
{

    public abstract class Identifier : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is IdentifierContext context)
            {
                this.LiteralName = context.GetRawText();
            }
            else if (ctx is TypeSpecifierContext context2)
            {
                this.LiteralName = context2.GetRawText();
            }
            else if (ctx is IdentifierWithGenericContext context3)
            {
                this.LiteralName = context3.GetRawText();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.identifier(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public abstract bool IsType { get; }

        public string LiteralName { get; set; } = "";

        public string Name => LiteralName;

        public override string BuildText()
        {
            return LiteralName;
        }
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