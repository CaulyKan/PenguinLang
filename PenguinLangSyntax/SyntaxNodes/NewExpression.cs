namespace PenguinLangSyntax.SyntaxNodes
{

    public class NewExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is NewExpressionContext context)
            {
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x))
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.newExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; set; }

        [ChildrenNode]
        public List<Expression> ArgumentsExpression { get; set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubNewExpression = this,
                PostfixExpressionType = PostfixExpression.Type.New
            };
        }

        public override string BuildSourceText()
        {
            var args = ArgumentsExpression.Count > 0
                ? $"({string.Join(", ", ArgumentsExpression.Select(e => e.BuildSourceText()))})"
                : "()";
            return $"new {TypeSpecifier!.BuildSourceText()}{args}";
        }
    }
}