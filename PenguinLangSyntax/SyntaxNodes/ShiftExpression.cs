namespace PenguinLangSyntax.SyntaxNodes
{

    public class ShiftExpression : SyntaxNode, ISyntaxExpression
    {
        [ChildrenNode]
        public List<ISyntaxExpression> SubExpressions { get; set; } = [];

        public List<BinaryOperatorEnum> Operators { get; set; } = [];

        public bool IsSimple => SubExpressions.Count == 1 && SubExpressions[0].IsSimple;

        public ISyntaxExpression GetEffectiveExpression() => SubExpressions.Count == 1 ? (SubExpressions[0] as ISyntaxExpression).GetEffectiveExpression() : this;

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.shiftExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ShiftExpressionContext context)
            {
                SubExpressions = context.children.OfType<AdditiveExpressionContext>()
                   .Select(x => Build<AdditiveExpression>(walker, x).GetEffectiveExpression())
                   .ToList();
                Operators = context.shiftOperator().Select(x => x.GetText() switch
                    {
                        "<<" => BinaryOperatorEnum.LeftShift,
                        ">>" => BinaryOperatorEnum.RightShift,
                        _ => throw new System.NotImplementedException("Invalid shift operator")
                    }).ToList();
            }
            else throw new NotImplementedException();
        }

        public override string BuildSourceText()
        {
            if (SubExpressions.Count == 1)
            {
                return SubExpressions[0].BuildSourceText();
            }

            var result = new List<string>();
            result.Add(SubExpressions[0].BuildSourceText());

            for (int i = 0; i < Operators.Count; i++)
            {
                var op = Operators[i] switch
                {
                    BinaryOperatorEnum.LeftShift => "<<",
                    BinaryOperatorEnum.RightShift => ">>",
                    _ => throw new NotImplementedException($"Unsupported shift operator: {Operators[i]}")
                };
                result.Add(op);
                result.Add(SubExpressions[i + 1].BuildSourceText());
            }

            return string.Join(" ", result);
        }
    }
}