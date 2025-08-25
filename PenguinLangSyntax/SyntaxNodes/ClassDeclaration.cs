namespace PenguinLangSyntax.SyntaxNodes
{

    public class ClassDeclaration : Declaration
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is ClassDeclarationContext context)
            {
                Identifier = Build<SymbolIdentifier>(walker, context.identifier());
                if (context.typeSpecifier() != null)
                {
                    TypeSpecifier = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }
                else
                {
                    throw new NotImplementedException("Type infer is not supported yet");
                }

                IsMutable = context.typeMutabilitySpecifier()?.GetText() switch
                {
                    null => Mutability.Unspecified,
                    "auto" => Mutability.Auto,
                    "mut" => Mutability.Mutable,
                    "!mut" => Mutability.Immutable,
                    _ => throw new Exception("Invalid mutability specifier")
                };

                if (context.expression() != null)
                    InitializeExpression = Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.classDeclaration(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public Mutability IsMutable { get; set; } = Mutability.Immutable;

        public override string BuildText()
        {
            var mutStr = "";
            if (IsMutable == Mutability.Mutable)
                mutStr = "mut ";
            else if (IsMutable == Mutability.Immutable)
                mutStr = "!mut ";

            var parts = new List<string>();
            parts.Add(Identifier!.BuildText());
            parts.Add(":");
            parts.Add(mutStr);
            parts.Add(TypeSpecifier!.BuildText());
            if (InitializeExpression != null)
            {
                parts.Add("=");
                parts.Add(InitializeExpression.BuildText());
            }
            return string.Join(" ", parts);
        }
    }
}