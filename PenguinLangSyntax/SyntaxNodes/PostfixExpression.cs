namespace PenguinLangSyntax.SyntaxNodes
{
    public class PostfixExpression : SyntaxNode, ISyntaxExpression
    {
        public enum Type
        {
            PrimaryExpression,
            // Slicing,
            FunctionCall,
            MemberAccess,
            New,
            Wait,
            SpawnAsync,
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PostfixExpressionContext context)
            {
                if (context.primaryExpression() != null)
                {
                    SubPrimaryExpression = Build<PrimaryExpression>(walker, context.primaryExpression());
                    PostfixExpressionType = Type.PrimaryExpression;
                }
                // else if (context.children.OfType<SlicingExpressionContext>().Any())
                // {
                //     SubSlicingExpression = new SlicingExpression(walker, context.slicingExpression());
                //     PostfixExpressionType = Type.Slicing;
                // }
                else if (context.functionCallExpression() != null)
                {
                    SubFunctionCallExpression = Build<FunctionCallExpression>(walker, context.functionCallExpression());
                    PostfixExpressionType = Type.FunctionCall;
                }
                else if (context.memberAccessExpression() != null)
                {
                    SubMemberAccessExpression = Build<ReadMemberAccessExpression>(walker, context.memberAccessExpression());
                    PostfixExpressionType = Type.MemberAccess;
                }
                else if (context.newExpression() != null)
                {
                    SubNewExpression = Build<NewExpression>(walker, context.newExpression());
                    PostfixExpressionType = Type.New;
                }
                else if (context.waitExpression() != null)
                {
                    SubWaitExpression = Build<WaitExpression>(walker, context.waitExpression());
                    PostfixExpressionType = Type.Wait;
                }
                else if (context.spawnExpression() != null)
                {
                    SubSpawnAsyncExpression = Build<SpawnAsyncExpression>(walker, context.spawnExpression());
                    PostfixExpressionType = Type.SpawnAsync;
                }
                else
                {
                    throw new NotImplementedException("Invalid postfix expression");
                }
            }
            else throw new NotImplementedException();
        }

        public Type PostfixExpressionType { get; private set; }

        [ChildrenNode]
        public PrimaryExpression? SubPrimaryExpression { get; private set; }

        // public SlicingExpression? SubSlicingExpression { get; private set; }

        [ChildrenNode]
        public FunctionCallExpression? SubFunctionCallExpression { get; private set; }

        [ChildrenNode]
        public MemberAccessExpression? SubMemberAccessExpression { get; private set; }

        [ChildrenNode]
        public NewExpression? SubNewExpression { get; private set; }

        [ChildrenNode]
        public WaitExpression? SubWaitExpression { get; private set; }

        [ChildrenNode]
        public SpawnAsyncExpression? SubSpawnAsyncExpression { get; private set; }

        public bool IsSimple => PostfixExpressionType == Type.PrimaryExpression && SubPrimaryExpression!.IsSimple;
    }
}