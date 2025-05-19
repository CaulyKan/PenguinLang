namespace PenguinLangSyntax.SyntaxNodes
{

    public class UnaryExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is UnaryExpressionContext context)
            {
                if (context.children.OfType<UnaryOperatorContext>().Any())
                {
                    UnaryOperator = context.unaryOperator().GetText() switch
                    {
                        "&" => (UnaryOperatorEnum?)UnaryOperatorEnum.Ref,
                        "*" => (UnaryOperatorEnum?)UnaryOperatorEnum.Deref,
                        "+" => (UnaryOperatorEnum?)UnaryOperatorEnum.Plus,
                        "-" => (UnaryOperatorEnum?)UnaryOperatorEnum.Minus,
                        "!" => (UnaryOperatorEnum?)UnaryOperatorEnum.LogicalNot,
                        "~" => (UnaryOperatorEnum?)UnaryOperatorEnum.BitwiseNot,
                        _ => throw new System.NotImplementedException("Invalid unary operator"),
                    };
                }
                SubExpression = Build<PostfixExpression>(walker, context.postfixExpression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public ISyntaxExpression GetEffectiveExpression() => HasUnaryOperator ? this : SubExpression!.GetEffectiveExpression();

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.unaryExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? SubExpression { get; set; }

        public UnaryOperatorEnum? UnaryOperator { get; private set; }

        public bool HasUnaryOperator => UnaryOperator is not null;

        public bool IsSimple => !HasUnaryOperator && SubExpression!.IsSimple;

        public override string BuildSourceText()
        {
            if (this.HasUnaryOperator)
            {
                var op = this.UnaryOperator! switch
                {
                    UnaryOperatorEnum.Ref => "&",
                    UnaryOperatorEnum.Deref => "*",
                    UnaryOperatorEnum.Plus => "+",
                    (UnaryOperatorEnum?)UnaryOperatorEnum.Minus => "-",
                    (UnaryOperatorEnum?)UnaryOperatorEnum.LogicalNot => "!",
                    (UnaryOperatorEnum?)UnaryOperatorEnum.BitwiseNot => "~",
                    _ => throw new System.NotImplementedException("Invalid unary operator"),
                };
                return $"{op}{SubExpression!.BuildSourceText()}";
            }
            else
                return SubExpression!.BuildSourceText();
        }
    }
}