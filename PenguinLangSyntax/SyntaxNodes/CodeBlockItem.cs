namespace PenguinLangSyntax.SyntaxNodes
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

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "<annoymous>", p => p.codeBlockItem(), reporter);
            var walker = new SyntaxWalker("<annoymous>", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Statement? Statement { get; set; }

        [ChildrenNode]
        public Declaration? Declaration { get; set; }

        [ChildrenNode]
        public TypeReferenceDeclaration? TypeReference { get; set; }

        public CodeBlockItemType Type { get; private set; }

        public override string BuildSourceText()
        {
            return Type switch
            {
                CodeBlockItemType.Statement => Statement!.BuildSourceText(),
                CodeBlockItemType.Declaration => Declaration!.BuildSourceText(),
                CodeBlockItemType.TypeReference => TypeReference!.BuildSourceText(),
                _ => throw new NotImplementedException($"Unsupported CodeBlockItemType: {Type}")
            };
        }
    }

}