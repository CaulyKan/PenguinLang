using Antlr4.Runtime.Tree;

namespace PenguinLangParser.SyntaxNodes
{

    public class WaitExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                Expression = context.expression().Length > 0 ? Build<Expression>(walker, context.expression(0)).GetEffectiveExpression() : null;
                IsTickUnit = context.children.Any(c => c is ITerminalNode t && t.Symbol.Text == "tick");
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
        public ISyntaxExpression? Expression { get; set; }

        public bool IsTickUnit { get; set; } = false;

        /// <summary>
        /// `wait change(x)` edge-detection sugar. The parser reads this as a
        /// call to a function named `change`; in wait position it instead means
        /// "park until the watched expression's value differs from its value
        /// at wait entry, then yield the new value" (`change` is not callable
        /// here — the classic idiom `let v = x; while (x == v) { wait x; }`).
        /// </summary>
        public bool IsChangeUnit =>
            Expression is FunctionCallExpression f
            && f.Callee?.GetEffectiveExpression() is PrimaryExpression { IsSimple: true } p
            && p.Text == "change"
            && f.ArgumentsExpression.Count == 1;

        /// <summary>The watched expression of the change form (precondition: IsChangeUnit).</summary>
        public ISyntaxExpression ChangeWatchedExpression =>
            ((FunctionCallExpression)Expression!).ArgumentsExpression[0];

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

        public override string ToShortString() => "wait";

        public override string BuildText()
        {
            if (Expression == null)
                return "wait";
            else if (IsTickUnit)
                return $"wait {Expression!.BuildText()} tick";
            else
                return $"wait {Expression!.BuildText()}";
        }
    }
}
