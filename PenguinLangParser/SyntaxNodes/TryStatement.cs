using System;
using System.Collections.Generic;
using System.Linq;

namespace PenguinLangParser.SyntaxNodes
{
    public class TryStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is TryStatementContext context)
            {
                var statements = context.children.OfType<StatementContext>().ToList();
                if (statements.Count == 2)
                {
                    TryBlock = Build<Statement>(walker, statements[0]);
                    CatchBlock = Build<Statement>(walker, statements[1]);
                }
                else
                {
                    throw new System.NotImplementedException("Invalid number of statements in try statement");
                }

                CatchDeclaration = Build<Declaration>(walker, context.declaration());
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.tryStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Statement? TryBlock { get; private set; }

        [ChildrenNode]
        public Declaration? CatchDeclaration { get; private set; }

        [ChildrenNode]
        public Statement? CatchBlock { get; private set; }

        /// <summary>
        /// Scope the catch variable is declared in: the catch block's own scope
        /// when the body is a brace block, otherwise the enclosing scope.
        /// </summary>
        public uint CatchScopeId =>
            CatchBlock is { StatementType: Statement.Type.SubBlock, CodeBlockExpression: { } cb } ? cb.BlockScopeId
            : CatchBlock?.ScopeId ?? ScopeId;

        public override string ToShortString() => "try";

        public override string BuildText()
        {
            var parts = new List<string>();
            parts.Add("try");
            parts.Add(TryBlock!.BuildText());
            parts.Add("catch");
            parts.Add("(" + CatchDeclaration!.BuildText() + ")");
            parts.Add(CatchBlock!.BuildText());
            return string.Join(" ", parts);
        }
    }
}
