namespace PenguinLangSyntax.SyntaxNodes
{

    public class PrimaryExpression : SyntaxNode, ISyntaxExpression
    {
        public enum Type
        {
            Identifier,
            Constant,
            StringLiteral,
            BoolLiteral,
            VoidLiteral,
            ParenthesizedExpression,
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is PrimaryExpressionContext context)
            {
                if (context.children.OfType<IdentifierWithGenericContext>().Any())
                {
                    Identifier = Build<SymbolIdentifier>(walker, context.identifierWithGeneric());
                    PrimaryExpressionType = Type.Identifier;
                }
                else if (context.Constant() != null)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.Constant;
                }
                else if (context.StringLiteral().Length > 0)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.StringLiteral;
                }
                else if (context.boolLiteral() != null)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.BoolLiteral;
                }
                else if (context.voidLiteral() != null)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.VoidLiteral;
                }
                else if (context.children.OfType<ExpressionContext>().Any())
                {
                    ParenthesizedExpression = Build<Expression>(walker, context.expression());
                    PrimaryExpressionType = Type.ParenthesizedExpression;
                }
                else
                {
                    throw new System.NotImplementedException("Invalid primary expression");
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.primaryExpression(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => PrimaryExpressionType switch
        {
            Type.Identifier => this,
            Type.Constant => this,
            Type.StringLiteral => this,
            Type.BoolLiteral => this,
            Type.VoidLiteral => this,
            Type.ParenthesizedExpression => ParenthesizedExpression!.GetEffectiveExpression(),
            _ => throw new NotImplementedException(),
        };

        public Type PrimaryExpressionType { get; set; }

        [ChildrenNode]
        public Identifier? Identifier { get; set; }

        public string? Literal { get; set; }

        [ChildrenNode]
        public Expression? ParenthesizedExpression { get; set; }

        public bool IsSimple => PrimaryExpressionType switch
        {
            Type.Identifier => true,
            Type.Constant => true,
            Type.StringLiteral => true,
            Type.BoolLiteral => true,
            Type.VoidLiteral => true,
            Type.ParenthesizedExpression => ParenthesizedExpression!.IsSimple,
            _ => throw new NotImplementedException("Invalid primary expression type"),
        };

        public ISyntaxExpression CreateWrapperExpression()
        {
            return new PostfixExpression
            {
                Text = this.Text,
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SubPrimaryExpression = this,
                PostfixExpressionType = PostfixExpression.Type.PrimaryExpression
            };
        }
    }
}