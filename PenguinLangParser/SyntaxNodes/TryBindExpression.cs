namespace PenguinLangParser.SyntaxNodes
{

    // `let a [: T] := b` — try-bind expression: a boolean that, when true,
    // binds `a` to the extracted payload / cast result of `b`.
    public class TryBindExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TryBindExpressionContext context)
            {
                VariableName = Build<SymbolIdentifier>(walker, context.identifier());
                var ts = context.typeSpecifier();
                if (ts != null) TypeSpecifier = Build<TypeSpecifier>(walker, ts);
                RHS = Build<BitWiseOrExpression>(walker, context.bitwiseOrExpression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override string ToShortString() => "trybind";

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.primaryExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? VariableName { get; set; }

        [ChildrenNode]
        public TypeSpecifier? TypeSpecifier { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? RHS { get; set; }

        public ISyntaxExpression GetEffectiveExpression() => this;

        public bool IsSimple => false;

        public override string BuildText()
        {
            var t = VariableName!.BuildText();
            if (TypeSpecifier != null) t += $": {TypeSpecifier.BuildText()}";
            return $"let {t} := {RHS!.BuildText()}";
        }
    }
}
