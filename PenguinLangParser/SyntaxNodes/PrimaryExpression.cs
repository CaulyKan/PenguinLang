namespace PenguinLangParser.SyntaxNodes
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
            LambdaFunction,
            ParenthesizedExpression,
            CodeBlockExpression,
            IfExpression,
            WhileExpression,
            Cast,
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
                else if (context.lambdaFunctionExpression() != null)
                {
                    LambdaFunction = Build<LambdaFunctionExpression>(walker, context.lambdaFunctionExpression());
                    PrimaryExpressionType = Type.LambdaFunction;
                }
                else if (context.codeBlockExpression() != null)
                {
                    CodeBlockExpression = Build<CodeBlockExpression>(walker, context.codeBlockExpression());
                    PrimaryExpressionType = Type.CodeBlockExpression;
                }
                else if (context.ifExpression() != null)
                {
                    IfExpression = Build<IfExpression>(walker, context.ifExpression());
                    PrimaryExpressionType = Type.IfExpression;
                }
                else if (context.whileExpression() != null)
                {
                    WhileExpression = Build<WhileExpression>(walker, context.whileExpression());
                    PrimaryExpressionType = Type.WhileExpression;
                }
                else if (context.typeSpecifier() != null && context.expression() != null
                    && context.children.OfType<ExpressionContext>().Any()
                    && context.lambdaFunctionExpression() == null)
                {
                    CastExpression = Build<CastExpression>(walker, context);
                    PrimaryExpressionType = Type.Cast;
                }
                else if (context.children.OfType<ExpressionContext>().Any())
                {
                    ParenthesizedExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
                    PrimaryExpressionType = Type.ParenthesizedExpression;
                }
                else
                {
                    throw new System.NotImplementedException("Invalid primary expression");
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.primaryExpression(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        public ISyntaxExpression GetEffectiveExpression() => PrimaryExpressionType switch
        {
            Type.Identifier => this,
            Type.Constant => this,
            Type.StringLiteral => this,
            Type.BoolLiteral => this,
            Type.VoidLiteral => this,
            Type.LambdaFunction => this,
            Type.ParenthesizedExpression => this,
            Type.CodeBlockExpression => this,
            Type.IfExpression => this,
            Type.WhileExpression => this,
            Type.Cast => CastExpression!,
            _ => throw new NotImplementedException(),
        };

        public Type PrimaryExpressionType { get; set; }

        [SexpValue]
        public string PrimaryType => PrimaryExpressionType.ToString();

        [ChildrenNode]
        public Identifier? Identifier { get; set; }

        [SexpValue]
        public string? Literal { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? ParenthesizedExpression { get; set; }

        [ChildrenNode]
        public LambdaFunctionExpression? LambdaFunction { get; set; }

        [ChildrenNode]
        public CodeBlockExpression? CodeBlockExpression { get; set; }

        [ChildrenNode]
        public IfExpression? IfExpression { get; set; }

        [ChildrenNode]
        public WhileExpression? WhileExpression { get; set; }

        [ChildrenNode]
        public CastExpression? CastExpression { get; set; }

        public bool IsSimple => PrimaryExpressionType switch
        {
            Type.Identifier => true,
            Type.Constant => true,
            Type.StringLiteral => true,
            Type.BoolLiteral => true,
            Type.VoidLiteral => true,
            Type.ParenthesizedExpression => ParenthesizedExpression!.IsSimple,
            Type.CodeBlockExpression => false,
            Type.IfExpression => false,
            Type.WhileExpression => false,
            Type.Cast => false,
            _ => throw new NotImplementedException("Invalid primary expression type"),
        };

        public override string ToShortString() => "";

        public override string BuildText()
        {
            return PrimaryExpressionType switch
            {
                Type.Identifier => Identifier!.BuildText(),
                Type.Constant => Literal!,
                Type.StringLiteral => Literal!,
                Type.BoolLiteral => Literal!,
                Type.VoidLiteral => Literal!,
                Type.LambdaFunction => LambdaFunction!.BuildText(),
                Type.ParenthesizedExpression => $"({ParenthesizedExpression!.BuildText()})",
                Type.CodeBlockExpression => CodeBlockExpression!.BuildText(),
                Type.IfExpression => IfExpression!.BuildText(),
                Type.WhileExpression => WhileExpression!.BuildText(),
                Type.Cast => CastExpression!.BuildText(),
                _ => throw new NotImplementedException($"Unsupported PrimaryExpressionType: {PrimaryExpressionType}")
            };
        }
    }
}