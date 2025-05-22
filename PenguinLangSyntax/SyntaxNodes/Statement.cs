namespace PenguinLangSyntax.SyntaxNodes
{
    public class Statement : SyntaxNode
    {
        public enum Type
        {
            Empty,
            SubBlock,
            ExpressionStatement,
            IfStatement,
            WhileStatement,
            ForStatement,
            JumpStatement,
            AssignmentStatement,
            ReturnStatement,
            YieldStatement,
            SignalStatement,
            EmitEventStatement,
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
                else if (context.signalStatement() is not null)
                {
                    StatementType = Type.SignalStatement;
                    SignalStatement = Build<SignalStatement>(walker, context.signalStatement());
                }
                else if (context.emitEventStatement() is not null)
                {
                    StatementType = Type.EmitEventStatement;
                    EmitEventStatement = Build<EmitEventStatement>(walker, context.emitEventStatement());
                }
                else if (context.codeBlock() is not null)
                {
                    StatementType = Type.SubBlock;
                    CodeBlock = Build<CodeBlock>(walker, context.codeBlock());
                }
                else if (context.GetText() == ";")
                {
                    StatementType = Type.Empty;
                }
                else throw new NotImplementedException();

            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.statement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public Type StatementType { get; set; }

        [ChildrenNode]
        public CodeBlock? CodeBlock { get; set; }

        [ChildrenNode]
        public ExpressionStatement? ExpressionStatement { get; set; }

        [ChildrenNode]
        public IfStatement? IfStatement { get; set; }

        [ChildrenNode]
        public ForStatement? ForStatement { get; set; }

        [ChildrenNode]
        public WhileStatement? WhileStatement { get; set; }

        [ChildrenNode]
        public JumpStatement? JumpStatement { get; set; }

        [ChildrenNode]
        public AssignmentStatement? AssignmentStatement { get; set; }

        [ChildrenNode]
        public ReturnStatement? ReturnStatement { get; set; }

        [ChildrenNode]
        public YieldStatement? YieldStatement { get; set; }

        [ChildrenNode]
        public SignalStatement? SignalStatement { get; set; }

        [ChildrenNode]
        public EmitEventStatement? EmitEventStatement { get; set; }

        public override string BuildText()
        {
            return StatementType switch
            {
                Type.Empty => ";",
                Type.SubBlock => CodeBlock!.BuildText(),
                Type.ExpressionStatement => ExpressionStatement!.BuildText(),
                Type.IfStatement => IfStatement!.BuildText(),
                Type.WhileStatement => WhileStatement!.BuildText(),
                Type.ForStatement => ForStatement!.BuildText(),
                Type.JumpStatement => JumpStatement!.BuildText(),
                Type.AssignmentStatement => AssignmentStatement!.BuildText(),
                Type.ReturnStatement => ReturnStatement!.BuildText(),
                Type.YieldStatement => YieldStatement!.BuildText(),
                Type.SignalStatement => SignalStatement!.BuildText(),
                Type.EmitEventStatement => EmitEventStatement!.BuildText(),
                _ => throw new NotImplementedException($"Unsupported StatementType: {StatementType}")
            };
        }
    }
}