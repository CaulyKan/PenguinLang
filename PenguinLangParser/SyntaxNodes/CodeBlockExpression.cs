namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// Represents a block expression. If a trailing expression is present,
    /// the block evaluates to the value of that expression.
    /// Example: let x: i64 = { let a = 1; let b = 2; a + b };
    /// Without trailing expression, the block evaluates to void (like a regular code block).
    /// </summary>
    public class CodeBlockExpression : SyntaxNode, ISyntaxScope, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is CodeBlockExpressionContext context)
            {
                walker.PushScope(SyntaxScopeType.CodeBlock, this);

                // Build block items (statements and declarations)
                BlockItems = context.codeBlockItem()
                    .Select(x => Build<CodeBlockItem>(walker, x)).ToList();

                // Build trailing expression (optional)
                if (context.expression() != null)
                    TrailingExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
                else
                    TrailingExpression = null;

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.codeBlockExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public List<CodeBlockItem> BlockItems { get; set; } = [];

        [ChildrenNode]
        public ISyntaxExpression? TrailingExpression { get; set; }

        [SexpValue]
        public SyntaxScopeType ScopeType => SyntaxScopeType.CodeBlock;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [SexpValue]
        public bool IsAnonymous => true;

        private static uint counter = 0;

        public string Name => $"__block_expr_{counter++}";

        public bool IsSimple => false;

        public override string ToShortString() => Name;

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("{\n");

            foreach (var item in BlockItems)
            {
                parts.Add(item.BuildText());
                parts.Add("\n");
            }

            // Trailing expression without semicolon (if present)
            if (TrailingExpression != null)
            {
                parts.Add(TrailingExpression.BuildText());
                parts.Add("\n");
            }

            parts.Add("}");

            return string.Join("", parts);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;
    }
}
