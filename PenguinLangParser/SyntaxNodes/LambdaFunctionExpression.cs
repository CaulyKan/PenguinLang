namespace PenguinLangParser.SyntaxNodes
{

    public class LambdaFunctionExpression : SyntaxNode, ISyntaxScope, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is LambdaFunctionExpressionContext context)
            {
                walker.PushScope(SyntaxScopeType.LambdaFunction, this);

                IsAsync = context.GetText().StartsWith("async_fun");

                if (context.parameterList()?.children == null)
                {
                    Parameters = [];
                }
                else
                {
                    Parameters = context.parameterList().children.OfType<DeclarationContext>()
                        .Select(x => Build<Declaration>(walker, x)).ToList();
                }

                if (context.typeSpecifier() == null)
                {
                    ReturnType = new TypeSpecifier
                    {
                        TypeName = "void",
                        IsIterable = false
                    };
                }
                else
                {
                    ReturnType = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }

                if (context.codeBlockExpression() != null)
                    CodeBlockExpression = Build<CodeBlockExpression>(walker, context.codeBlockExpression());

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.lambdaFunctionExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public List<Declaration> Parameters { get; set; } = [];

        [ChildrenNode]
        public TypeSpecifier? ReturnType { get; set; }

        [ChildrenNode]
        public CodeBlockExpression? CodeBlockExpression { get; set; }

        [SexpValue]
        public SyntaxScopeType ScopeType => SyntaxScopeType.Function;

        public List<SyntaxSymbol> Symbols { get; set; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [SexpValue]
        public bool IsAnonymous => true;

        [SexpValue]
        public bool IsAsync { get; set; }

        private static uint counter = 0;

        public string Name => $"__lambda_{counter++}";

        public bool IsSimple => false;

        public override string ToShortString() => Name;

        public override string BuildText()
        {
            var parts = new List<string>();
            if (IsAsync)
            {
                parts.Add("async_fun");
            }
            else
            {
                parts.Add("fun");
            }

            parts.Add("(");
            if (Parameters.Count > 0)
            {
                parts.Add(string.Join(", ", Parameters.Select(p => p.BuildText())));
            }
            parts.Add(")");

            if (ReturnType != null)
            {
                parts.Add("->");
                parts.Add(ReturnType.BuildText());
            }

            if (CodeBlockExpression != null)
            {
                parts.Add(CodeBlockExpression.BuildText());
            }

            return string.Join(" ", parts);
        }

        public ISyntaxExpression GetEffectiveExpression() => this;
    }
}