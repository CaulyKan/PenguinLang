namespace PenguinLangParser.SyntaxNodes
{
    public class InitialRoutineDefinition : SyntaxNode, ISyntaxScope
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is InitialRoutineContext context)
            {
                walker.PushScope(SyntaxScopeType.InitialRoutine, this);

                CodeBlockExpression = Build<CodeBlockExpression>(walker, context.codeBlockExpression());
                Name = context.identifier() == null ? $"initial_{counter++}" : context.identifier().GetText();

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.initialRoutine(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        static UInt64 counter = 0;

        [ChildrenNode]
        public CodeBlockExpression? CodeBlockExpression { get; set; } = null;

        [SexpValue]
        public string Name { get; set; } = "";

        public SyntaxScopeType ScopeType => SyntaxScopeType.InitialRoutine;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }
        [SexpValue]
        public bool IsAnonymous => false;

        public override string ToShortString() => Name;

        public override string BuildText()
        {
            return $"initial {Name} {CodeBlockExpression!.BuildText()}";
        }
    }
}