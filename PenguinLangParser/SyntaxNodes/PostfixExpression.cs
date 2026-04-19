namespace PenguinLangParser.SyntaxNodes
{
    public class PostfixExpression : SyntaxNode, ISyntaxExpression
    {
        public enum Type
        {
            PrimaryExpression,
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
                else if (context.postfixExpression() != null && context.identifierWithGeneric() != null)
                {
                    // Member access: postfixExpression '.' identifierWithGeneric
                    SubMemberAccessExpression = Build<ReadMemberAccessExpression>(walker, context);
                    PostfixExpressionType = Type.MemberAccess;
                }
                else if (context.postfixExpression() != null)
                {
                    // Function call: postfixExpression '(' args ')'
                    SubFunctionCallExpression = Build<FunctionCallExpression>(walker, context);
                    PostfixExpressionType = Type.FunctionCall;
                }
                else if (context.typeSpecifier() != null)
                {
                    // New: 'new' typeSpecifier '(' args ')'
                    SubNewExpression = Build<NewExpression>(walker, context);
                    PostfixExpressionType = Type.New;
                }
                else if (context.Start.Text == "async")
                {
                    SubSpawnAsyncExpression = Build<SpawnAsyncExpression>(walker, context);
                    PostfixExpressionType = Type.SpawnAsync;
                }
                else if (context.Start.Text == "wait")
                {
                    SubWaitExpression = Build<WaitExpression>(walker, context);
                    PostfixExpressionType = Type.Wait;
                }
                else
                {
                    throw new NotImplementedException("Invalid postfix expression");
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.postfixExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => PostfixExpressionType switch
        {
            Type.PrimaryExpression => (SubPrimaryExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            Type.FunctionCall => SubFunctionCallExpression!.GetEffectiveExpression(),
            Type.MemberAccess => SubMemberAccessExpression!.GetEffectiveExpression(),
            Type.New => SubNewExpression!.GetEffectiveExpression(),
            Type.Wait => (SubWaitExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            Type.SpawnAsync => (SubSpawnAsyncExpression as ISyntaxExpression)!.GetEffectiveExpression(),
            _ => throw new NotImplementedException(),
        };

        [SexpValue]
        public Type PostfixExpressionType { get; set; }

        [ChildrenNode]
        public PrimaryExpression? SubPrimaryExpression { get; set; }

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

        public override string ToShortString() => "";

        public override string BuildText()
        {
            return PostfixExpressionType switch
            {
                Type.PrimaryExpression => SubPrimaryExpression!.BuildText(),
                Type.FunctionCall => SubFunctionCallExpression!.BuildText(),
                Type.MemberAccess => SubMemberAccessExpression!.BuildText(),
                Type.New => SubNewExpression!.BuildText(),
                Type.Wait => SubWaitExpression!.BuildText(),
                Type.SpawnAsync => SubSpawnAsyncExpression!.BuildText(),
                _ => throw new NotImplementedException($"Unsupported PostfixExpressionType: {PostfixExpressionType}")
            };
        }
    }
}
