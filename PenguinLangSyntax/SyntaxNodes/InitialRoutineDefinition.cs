namespace PenguinLangSyntax.SyntaxNodes
{
    public class InitialRoutineDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InitialRoutineContext context)
            {
                walker.PushScope(SyntaxScopeType.InitialRoutine, this);

                CodeBlock = Build<CodeBlock>(walker, context.codeBlock());
                Name = context.identifier() == null ? $"initial_{counter++}" : context.identifier().GetText();

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.initialRoutine(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        static UInt64 counter = 0;

        [ChildrenNode]
        public CodeBlock? CodeBlock { get; private set; } = null;

        public string Name { get; private set; } = "";

        public SyntaxScopeType ScopeType => SyntaxScopeType.InitialRoutine;

        public List<SyntaxSymbol> Symbols { get; private set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }
        public bool IsAnonymous => false;

        public override string BuildSourceText()
        {
            return $"initial {Name} {CodeBlock!.BuildSourceText()}";
        }
    }
}