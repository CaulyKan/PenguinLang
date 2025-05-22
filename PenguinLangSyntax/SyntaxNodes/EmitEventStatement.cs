using Antlr4.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PenguinLangSyntax.SyntaxNodes
{
    public class EmitEventStatement : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EmitEventStatementContext context)
            {
                EventIdentifier = Build<SymbolIdentifier>(walker, context.identifier());
                ArgumentExpression = context.expression() == null ? null : Build<Expression>(walker, context.expression()).GetEffectiveExpression();
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.emitEventStatement(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? EventIdentifier { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? ArgumentExpression { get; set; }

        public override string BuildText()
        {
            var args = ArgumentExpression is not null
                ? $"({ArgumentExpression.BuildText()})"
                : "";
            return $"emit {EventIdentifier!.BuildText()}{args};";
        }
    }
}