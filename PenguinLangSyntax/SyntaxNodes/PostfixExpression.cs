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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => PostfixExpressionType switch
        {
            Type.PrimaryExpression => (SubPrimaryExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            // Type.Slicing => SubSlicingExpression!.GetEffectiveExpression(),
            Type.FunctionCall => SubFunctionCallExpression!.GetEffectiveExpression(),
            Type.MemberAccess => SubMemberAccessExpression!.GetEffectiveExpression(),
            Type.New => SubNewExpression!.GetEffectiveExpression(),
            Type.Wait => (SubWaitExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            Type.SpawnAsync => (SubSpawnAsyncExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            _ => throw new NotImplementedException(),
        };

        public Type PostfixExpressionType { get; set; }

        [ChildrenNode]
        public PrimaryExpression? SubPrimaryExpression { get; set; }

        // public SlicingExpression? SubSlicingExpression { get; private set; }

        [ChildrenNode]
        public FunctionCallExpression? SubFunctionCallExpression { get; set; }

        [ChildrenNode]
        public MemberAccessExpression? SubMemberAccessExpression { get; set; }

        [ChildrenNode]
        public NewExpression? SubNewExpression { get; set; }

        [ChildrenNode]
        public WaitExpression? SubWaitExpression { get; set; }

        [ChildrenNode]
        public SpawnAsyncExpression? SubSpawnAsyncExpression { get; set; }

        public bool IsSimple => PostfixExpressionType == Type.PrimaryExpression && SubPrimaryExpression!.IsSimple;

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new UnaryExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubExpression = this,
            };
        }

        public override string BuildSourceText()
        {
            return PostfixExpressionType switch
            {
                Type.PrimaryExpression => SubPrimaryExpression!.BuildSourceText(),
                Type.FunctionCall => SubFunctionCallExpression!.BuildSourceText(),
                Type.MemberAccess => SubMemberAccessExpression!.BuildSourceText(),
                Type.New => SubNewExpression!.BuildSourceText(),
                Type.Wait => SubWaitExpression!.BuildSourceText(),
                Type.SpawnAsync => SubSpawnAsyncExpression!.BuildSourceText(),
                _ => throw new NotImplementedException($"Unsupported PostfixExpressionType: {PostfixExpressionType}")
            };
        }
    }
}