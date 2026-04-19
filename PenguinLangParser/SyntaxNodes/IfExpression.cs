namespace PenguinLangParser.SyntaxNodes
{
    /// <summary>
    /// Represents an if expression that evaluates to the value of the executed branch.
    /// Example: let x: i64 = if (cond) { 1 } else { 2 };
    /// </summary>
    public class IfExpression : SyntaxNode, ISyntaxExpression
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is IfExpressionContext context)
            {
                Condition = Build<Expression>(walker, context.expression()).GetEffectiveExpression();

                // Main branch is always a codeBlockExpression
                MainBranch = Build<CodeBlockExpression>(walker, context.codeBlockExpression(0));

                // Else branch can be codeBlockExpression or another ifExpression
                if (context.codeBlockExpression().Length > 1)
                {
                    // else { ... }
                    ElseBranch = Build<CodeBlockExpression>(walker, context.codeBlockExpression(1));
                    ElseBranchExpression = null;
                }
                else if (context.ifExpression() != null)
                {
                    // else if ...
                    ElseBranchExpression = Build<IfExpression>(walker, context.ifExpression());
                    ElseBranch = null;
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.ifExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public ISyntaxExpression? Condition { get; private set; }

        [ChildrenNode]
        public CodeBlockExpression? MainBranch { get; private set; }

        [ChildrenNode]
        public CodeBlockExpression? ElseBranch { get; private set; }

        [ChildrenNode]
        public IfExpression? ElseBranchExpression { get; private set; }

        [SexpValue]
        public bool HasElse => ElseBranch is not null || ElseBranchExpression is not null;

        public bool IsSimple => false;

        public ISyntaxExpression GetEffectiveExpression() => this;

        public override string ToShortString() => "if";

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("if");
            parts.Add("(" + Condition!.BuildText() + ")");
            parts.Add(MainBranch!.BuildText());

            if (HasElse)
            {
                parts.Add("else");
                if (ElseBranch != null)
                    parts.Add(ElseBranch.BuildText());
                else if (ElseBranchExpression != null)
                    parts.Add(ElseBranchExpression.BuildText());
            }

            return string.Join(" ", parts);
        }
    }
}
