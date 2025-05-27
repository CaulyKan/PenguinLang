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
                var expression = context.expression();
                EventExpression = Build<Expression>(walker, expression[0]);
                if (expression.Length > 1)
                    ArgumentExpression = Build<Expression>(walker, expression[1]).GetEffectiveExpression();
                else ArgumentExpression = null;
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
        public ISyntaxExpression? EventExpression { get; set; }

        [ChildrenNode]
        public ISyntaxExpression? ArgumentExpression { get; set; }

        public override string BuildText()
        {
            var args = ArgumentExpression is not null
                ? $"({ArgumentExpression.BuildText()})"
                : "";
            return $"emit {EventExpression!.BuildText()}{args};";
        }
    }
}