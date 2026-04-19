namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// Represents a while expression that can return a value via break statements.
    /// Example: let x: i64 = while (cond) { break 42; };
    /// </summary>
    public class WhileExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is WhileExpressionContext context)
            {
                Condition = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
                Body = Build<CodeBlockExpression>(walker, context.codeBlockExpression());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.whileExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Condition { get; private set; }

        [ChildrenNode]
        public CodeBlockExpression? Body { get; private set; }

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

        public override string ToShortString() => "while";

        public override string BuildText()
        {
            return $"while ({Condition!.BuildText()}) {Body!.BuildText()}";
        }
    }
}
