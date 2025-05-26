namespace PenguinLangSyntax.SyntaxNodes
{

    public class CodeBlock : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is CodeBlockContext context)
            {
                walker.PushScope(SyntaxScopeType.CodeBlock, this);
                BlockItems = context.children.OfType<CodeBlockItemContext>()
                    .Select(x => Build<CodeBlockItem>(walker, x)).ToList();
                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.codeBlock(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        static ulong counter = 0;

        [ChildrenNode]
        public List<CodeBlockItem> BlockItems { get; private set; } = [];

        public string Name { get; private set; } = $"codeblock_{counter++}";

        public SyntaxScopeType ScopeType => SyntaxScopeType.CodeBlock;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous => true;

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("{\n");
            if (BlockItems.Count > 0)
            {
                parts.Add(string.Join("\n", BlockItems.Select(item => item.BuildText())));
            }
            parts.Add("}\n");
            return string.Join("\n", parts);
        }
    }

}