namespace PenguinLangParser.SyntaxNodes
{

    public class GenericDefinitions : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);
            TypeParameters = [];
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            TypeParameters = [];
        }

        [ChildrenNode]
        public List<Identifier> TypeParameters { get; private set; } = [];

        public override string ToShortString() => "";

        public override string BuildText()
        {
            if (TypeParameters.Count == 0)
            {
                return "";
            }

            return $"<{string.Join(", ", TypeParameters.Select(p => p.BuildText()))}>";
        }
    }
}
