namespace PenguinLangSyntax.SyntaxNodes
{
    public class ReturnStatement : SyntaxNode
    {
        [ChildrenNode]
        public Expression? ReturnExpression { get; set; }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.returnStatement(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ReturnStatementContext context)
            {
                ReturnExpression = context.expression() is not null ? Build<Expression>(walker, context.expression()) : null;
                ReturnType = context.returnKeyword().GetText() switch
                {
                    "return" => ReturnTypeEnum.Normal,
                    "__yield_not_finished_return" => ReturnTypeEnum.YieldNotFinished,
                    "__yield_finished_return" => ReturnTypeEnum.YieldFinished,
                    "__blocked_return" => ReturnTypeEnum.Blocked,
                    _ => throw new NotImplementedException()
                };
            }
            else throw new NotImplementedException();
        }

        public enum ReturnTypeEnum
        {
            Normal,
            YieldNotFinished,
            YieldFinished,
            Blocked,
        }

        public ReturnTypeEnum ReturnType { get; set; } = ReturnTypeEnum.Normal;
    }

}