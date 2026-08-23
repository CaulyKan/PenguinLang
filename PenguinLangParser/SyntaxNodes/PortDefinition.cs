namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// RTL-style module port declaration inside a class body:
    /// `input x : T (= default)?;` / `output y : T (= default)?;`
    /// Desugars to a reference field of type mut __builtin.ISource&lt;T&gt; (input)
    /// or mut __builtin.ISink&lt;T&gt; (output); the direction is tracked for the
    /// permission matrix and connect wiring.
    /// </summary>
    public class PortDefinition : SyntaxNode
    {
        public bool IsInput { get; set; }

        public string Name { get; set; } = "";

        [ChildrenNode]
        public TypeSpecifier? Type { get; set; }

        /// <summary>Explicit default payload (`= expr`) — input only.</summary>
        [ChildrenNode]
        public ISyntaxExpression? DefaultExpression { get; set; }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PortDeclarationContext context)
            {
                IsInput = context.Start.Text == "input";
                Name = context.identifier().GetText();
                if (context.typeSpecifier() != null)
                    Type = Build<TypeSpecifier>(walker, context.typeSpecifier());
                if (context.expression() != null)
                    DefaultExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.portDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public override string ToShortString() => (IsInput ? "input " : "output ") + Name;

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add(IsInput ? "input" : "output");
            parts.Add(Name);
            if (Type != null)
            {
                parts.Add(":");
                parts.Add(Type.BuildText());
            }
            if (DefaultExpression != null)
            {
                parts.Add("=");
                parts.Add(DefaultExpression.BuildText());
            }
            parts.Add(";");
            return string.Join(" ", parts);
        }
    }
}
