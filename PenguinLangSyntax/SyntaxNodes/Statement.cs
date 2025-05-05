namespace PenguinLangSyntax.SyntaxNodes
{
    public class Statement : SyntaxNode
    {
        public enum Type
        {
            SubBlock,
            ExpressionStatement,
            IfStatement,
            WhileStatement,
            ForStatement,
            JumpStatement,
            AssignmentStatement,
            ReturnStatement,
            YieldStatement,
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is StatementContext context)
            {
                if (context.expressionStatement() is not null)
                {
                    StatementType = Type.ExpressionStatement;
                    ExpressionStatement = Build<ExpressionStatement>(walker, context.expressionStatement());
                }
                else if (context.ifStatement() is not null)
                {
                    StatementType = Type.IfStatement;
                    IfStatement = Build<IfStatement>(walker, context.ifStatement());
                }
                else if (context.whileStatement() is not null)
                {
                    StatementType = Type.WhileStatement;
                    WhileStatement = Build<WhileStatement>(walker, context.whileStatement());
                }
                else if (context.forStatement() is not null)
                {
                    StatementType = Type.ForStatement;
                    ForStatement = Build<ForStatement>(walker, context.forStatement());
                }
                else if (context.jumpStatement() is not null)
                {
                    StatementType = Type.JumpStatement;
                    JumpStatement = Build<JumpStatement>(walker, context.jumpStatement());
                }
                else if (context.assignmentStatement() is not null)
                {
                    StatementType = Type.AssignmentStatement;
                    AssignmentStatement = Build<AssignmentStatement>(walker, context.assignmentStatement());
                }
                else if (context.returnStatement() is not null)
                {
                    StatementType = Type.ReturnStatement;
                    ReturnStatement = Build<ReturnStatement>(walker, context.returnStatement());
                }
                else if (context.yieldStatement() is not null)
                {
                    StatementType = Type.YieldStatement;
                    YieldStatement = Build<YieldStatement>(walker, context.yieldStatement());
                }
                else
                {
                    StatementType = Type.SubBlock;
                    CodeBlock = Build<CodeBlock>(walker, context.codeBlock());
                }

            }
            else throw new NotImplementedException();
        }

        public Type StatementType { get; private set; }

        [ChildrenNode]
        public CodeBlock? CodeBlock { get; private set; }

        [ChildrenNode]
        public ExpressionStatement? ExpressionStatement { get; private set; }

        [ChildrenNode]
        public IfStatement? IfStatement { get; private set; }

        [ChildrenNode]
        public ForStatement? ForStatement { get; private set; }

        [ChildrenNode]
        public WhileStatement? WhileStatement { get; private set; }

        [ChildrenNode]
        public JumpStatement? JumpStatement { get; private set; }

        [ChildrenNode]
        public AssignmentStatement? AssignmentStatement { get; private set; }

        [ChildrenNode]
        public ReturnStatement? ReturnStatement { get; private set; }

        [ChildrenNode]
        public YieldStatement? YieldStatement { get; private set; }
    }

}