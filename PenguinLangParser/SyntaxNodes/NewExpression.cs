namespace PenguinLangParser.SyntaxNodes
{

    public class NewExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                // 'new mut T()' / 'new !mut T()' is invalid PenguinLang: a `new`
                // expression must not carry a mutability specifier on its type
                // (mutability belongs on the binding, e.g. `let x: mut T = new T()`).
                // Allowing it made `new mut T()` silently construct a value-typed
                // inline object, which mismatches ref-typed buffers (the combined.ll
                // header-loss native crash).
                if (TypeSpecifier != null && TypeSpecifier.IsMutable != Mutability.Auto)
                {
                    throw new PenguinLangException(
                        "'new' expression cannot have a mutability specifier (e.g. 'new mut T()'). Use 'new T()' instead.",
                        SourceLocation.ToString(),
                        ErrorCode.E_PARSE);
                }
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => Build<Expression>(walker, x).GetEffectiveExpression())
                   .ToList();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; set; }

        [ChildrenNode]
        public List<ISyntaxExpression> ArgumentsExpression { get; set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string ToShortString() => "new";

        public override string BuildText()
        {
            var args = ArgumentsExpression.Count > 0
                ? $"({string.Join(", ", ArgumentsExpression.Select(e => e.BuildText()))})"
                : "()";
            return $"new {TypeSpecifier!.BuildText()}{args}";
        }
    }
}
