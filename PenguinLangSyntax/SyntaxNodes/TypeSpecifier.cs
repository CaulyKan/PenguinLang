namespace PenguinLangSyntax.SyntaxNodes
{

    public class TypeSpecifier : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TypeSpecifierContext context)
            {
                var type = context.typeSpecifierWithoutIterable();
                var n = type.GetText();
                if (type.identifierWithDotsAndGenericArguments() != null && type.identifierWithDotsAndGenericArguments().Length > 0)
                {
                    TypeName = string.Join(".", type.identifierWithDotsAndGenericArguments().Select(i => parseIdentifierWithDotsAndGenericArguments(walker, i)));
                }
                else if (type.genericArguments() != null)
                {
                    var genericArguments = new GenericArguments();
                    genericArguments.Build(walker, type.genericArguments());
                    TypeName = (type.GetText().StartsWith("fun") ? "fun" : "async_fun") + genericArguments.BuildText();
                }
                else
                {
                    TypeName = type.GetText();
                }

                IsMutable = context.typeMutabilitySpecifier()?.GetText() switch
                {
                    null => Mutability.Auto,
                    "auto" => Mutability.Auto,
                    "mut" => Mutability.Mutable,
                    "!mut" => Mutability.Immutable,
                    _ => throw new Exception("Invalid mutability specifier")
                };
                IsIterable = context.iterableType() != null;

                if (IsMutable == Mutability.Mutable)
                    TypeName = "mut " + TypeName;
                else if (IsMutable == Mutability.Immutable)
                    TypeName = "!mut " + TypeName;
            }
            else throw new NotImplementedException();
        }

        private string parseIdentifierWithDotsAndGenericArguments(SyntaxWalker walker, IdentifierWithDotsAndGenericArgumentsContext context)
        {
            var identifierList = new List<string>();
            foreach (var identifier in context.identifierWithDots().identifier())
            {
                var id = new TypeIdentifier();
                id.Build(walker, identifier);
                identifierList.Add(id.BuildText());
            }
            var identifierWithDotsStr = string.Join(".", identifierList);
            if (context.genericArguments() != null)
            {
                var genericArguments = new GenericArguments();
                genericArguments.Build(walker, context.genericArguments());
                return $"{identifierWithDotsStr}{genericArguments.BuildText()}";
            }
            else
            {
                return identifierWithDotsStr;
            }
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.typeSpecifier(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public Mutability IsMutable { get; set; } = Mutability.Auto;

        public string TypeName { get; set; } = "";

        public string Name => IsIterable ? $"mut __builtin.IIterator<{TypeName}>" : TypeName;

        public bool IsIterable { get; set; } = false;

        public override string BuildText()
        {
            return IsIterable ? $"mut __builtin.IIterator<{TypeName}>" : TypeName;
        }
    }
}