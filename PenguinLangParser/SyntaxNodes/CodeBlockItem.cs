namespace PenguinLangParser.SyntaxNodes
{

    public class CodeBlockItem : SyntaxNode
    {
        public enum CodeBlockItemType
        {
            Statement,
            Declaration,
            TypeReference,
        }

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is CodeBlockItemContext context)
            {
                if (context.statement() is not null)
                {
                    Statement = Build<Statement>(walker, context.statement());
                    Type = CodeBlockItemType.Statement;
                }
                else if (context.declaration() is not null)
                {
                    Declaration = Build<Declaration>(walker, context.declaration());
                    Type = CodeBlockItemType.Declaration;
                }
                else if (context.typeReferenceDeclaration() is not null)
                {
                    TypeReference = Build<TypeReferenceDeclaration>(walker, context.typeReferenceDeclaration());
                    Type = CodeBlockItemType.TypeReference;
                }
                else throw new NotImplementedException();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.codeBlockItem(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Statement? Statement { get; set; }

        [ChildrenNode]
        public Declaration? Declaration { get; set; }

        [ChildrenNode]
        public TypeReferenceDeclaration? TypeReference { get; set; }

        public override string ToShortString() => "";

        [SexpValue]
        public CodeBlockItemType Type { get; private set; }

        public override string BuildText()
        {
            return Type switch
            {
                CodeBlockItemType.Statement => Statement!.BuildText(),
                // The grammar requires the letKeyword for block-level
                // declarations (`letKeyword declaration ';'`), so the
                // regenerated text must carry the `let` (and `mut`) prefix.
                CodeBlockItemType.Declaration => (Declaration!.SuggestMutableTypeInfer ? "let mut " : "let ") + Declaration!.BuildText() + ";",
                CodeBlockItemType.TypeReference => TypeReference!.BuildText() + ";",
                _ => throw new NotImplementedException($"Unsupported CodeBlockItemType: {Type}")
            };
        }
    }

}