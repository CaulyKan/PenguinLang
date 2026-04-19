using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antlr4.Runtime;

namespace PenguinLangParser.SyntaxNodes
{
    public class OnRoutineDefinition : SyntaxNode, ISyntaxScope
    {
        [ChildrenNode]
        public Expression? EventExpression { get; set; }

        [ChildrenNode]
        public Declaration? Parameter { get; set; }

        [ChildrenNode]
        public CodeBlockExpression? CodeBlockExpression { get; set; }

        public string Name => $"on_{counter++}";

        private static ulong counter = 0;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is OnRoutineContext context)
            {
                walker.PushScope(SyntaxScopeType.Function, this);

                EventExpression = Build<Expression>(walker, context.expression());
                if (context.declarationWithoutInitializer() != null)
                {
                    Parameter = Build<Declaration>(walker, context.declarationWithoutInitializer());
                }
                CodeBlockExpression = Build<CodeBlockExpression>(walker, context.codeBlockExpression());

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override string ToShortString() => "on";

        public override string BuildText()
        {
            var sb = new StringBuilder();
            sb.Append("on ");
            sb.Append(EventExpression!.BuildText());
            if (Parameter != null)
            {
                sb.Append("(");
                sb.Append(Parameter.BuildText());
                sb.Append(")");
            }
            sb.Append(" ");
            sb.Append(CodeBlockExpression!.BuildText());
            return sb.ToString();
        }

        public override void FromString(string text, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(text, "anonymous", p => p.onRoutine(), reporter);
            var walker = new SyntaxWalker("anonymous", reporter);
            Build(walker, syntaxNode);
        }

        [SexpValue]
        public SyntaxScopeType ScopeType => SyntaxScopeType.OnRoutine;

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        [SexpValue]
        public bool IsAnonymous => true;
    }
}