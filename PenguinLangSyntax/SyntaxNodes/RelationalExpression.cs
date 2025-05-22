namespace PenguinLangSyntax.SyntaxNodes
{

    public class RelationalExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public List<BinaryOperatorEnum> Operators { get; private set; } = [];

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.relationalExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is RelationalExpressionContext context)
            {
                SubExpressions = context.children.OfType<ShiftExpressionContext>()
                   .Select(x => Build<ShiftExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
                Operators = context.relationalOperator().Select(x => x.GetText() switch
                    {
                        "<" => BinaryOperatorEnum.LessThan,
                        ">" => BinaryOperatorEnum.GreaterThan,
                        "<=" => BinaryOperatorEnum.LessThanOrEqual,
                        ">=" => BinaryOperatorEnum.GreaterThanOrEqual,
                        _ => throw new System.NotImplementedException("Invalid relational operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }

        public override string BuildText()
        {
            if (SubExpressions.Count == 1)
            {
                return SubExpressions[0].BuildText();
            }

            var result = new List<string>();
            result.Add(SubExpressions[0].BuildText());

            for (int i = 0; i < Operators.Count; i++)
            {
                var op = Operators[i] switch
                {
                    BinaryOperatorEnum.LessThan => "<",
                    BinaryOperatorEnum.GreaterThan => ">",
                    BinaryOperatorEnum.LessThanOrEqual => "<=",
                    BinaryOperatorEnum.GreaterThanOrEqual => ">=",
                    _ => throw new NotImplementedException($"Unsupported relational operator: {Operators[i]}")
                };
                result.Add(op);
                result.Add(SubExpressions[i + 1].BuildText());
            }

            return string.Join(" ", result);
        }
    }
}