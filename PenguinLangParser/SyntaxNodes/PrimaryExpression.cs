namespace PenguinLangParser.SyntaxNodes
{

    public class PrimaryExpression : SyntaxNode, ISyntaxExpression
    {
        /// <summary>
        /// Normalize an integer literal's token text to plain DECIMAL (hex 0x/0X,
        /// binary 0b/0B, octal leading-0 parse to their u64 value). Decimal and
        /// non-integer-shaped text pass through unchanged. Must happen at PARSE
        /// time, before any consumer: ResolveLiteralType/MakeValue only parse
        /// decimal, and the C# backend emits constant text verbatim.
        /// </summary>
        public static string NormalizeIntegerLiteral(string text)
        {
            if (text.Length < 2 || !char.IsDigit(text[0]))
                return text;
            int radix;
            int start;
            if ((text[1] == 'x' || text[1] == 'X') && text.Length > 2) { radix = 16; start = 2; }
            else if ((text[1] == 'b' || text[1] == 'B') && text.Length > 2) { radix = 2; start = 2; }
            else if (text[0] == '0') { radix = 8; start = 1; }
            else return text;
            try
            {
                var value = Convert.ToUInt64(text[start..], radix);
                return value.ToString();
            }
            catch (FormatException)
            {
                return text;
            }
            catch (OverflowException)
            {
                return text;
            }
        }

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
            TryBind,
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
                    Literal = NormalizeIntegerLiteral(context.GetText());
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
                else if (context.tryBindExpression() != null)
                {
                    TryBindExpression = Build<TryBindExpression>(walker, context.tryBindExpression());
                    PrimaryExpressionType = Type.TryBind;
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
            Type.TryBind => TryBindExpression!,
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

        [ChildrenNode]
        public TryBindExpression? TryBindExpression { get; set; }

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
            Type.TryBind => false,
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
                Type.TryBind => TryBindExpression!.BuildText(),
                _ => throw new NotImplementedException($"Unsupported PrimaryExpressionType: {PrimaryExpressionType}")
            };
        }
    }
}