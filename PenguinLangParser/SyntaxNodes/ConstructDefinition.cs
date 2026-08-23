namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// Elaboration block: `construct { ... }` — the only scope where `connect`
    /// is legal. Top-level constructs run before any initial routine; lets
    /// declared inside are hoisted into the enclosing namespace (same symbol
    /// space as ordinary top-level lets).
    /// </summary>
    public class ConstructDefinition : SyntaxNode
    {
        [ChildrenNode]
        public CodeBlockExpression? Body { get; set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ConstructBlockContext context)
            {
                Body = Build<CodeBlockExpression>(walker, context.codeBlockExpression());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.constructBlock(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public override string ToShortString() => "construct";

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("construct");
            parts.Add(Body!.BuildText());
            return string.Join(" ", parts);
        }
    }
}
