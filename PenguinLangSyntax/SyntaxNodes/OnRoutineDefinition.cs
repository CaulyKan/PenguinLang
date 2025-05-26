using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antlr4.Runtime;

namespace PenguinLangSyntax.SyntaxNodes
{
    public class OnRoutineDefinition : SyntaxNode, ISyntaxScope
    {
        [ChildrenNode]
        public SymbolIdentifier? EventName { get; set; }

        [ChildrenNode]
        public Declaration? Parameter { get; set; }

        [ChildrenNode]
        public CodeBlock? Body { get; set; }

        public string Name => "on_" + EventName!.Text;

        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is OnRoutineContext context)
            {
                walker.PushScope(SyntaxScopeType.Function, this);

                EventName = Build<SymbolIdentifier>(walker, context.identifier());
                if (context.declarationWithoutInitializer() != null)
                {
                    Parameter = Build<Declaration>(walker, context.declarationWithoutInitializer());
                }
                Body = Build<CodeBlock>(walker, context.codeBlock());

                walker.PopScope();
            }
            else throw new NotImplementedException();
        }

        public override string BuildText()
        {
            var sb = new StringBuilder();
            sb.Append("on ");
            sb.Append(EventName!.BuildText());
            if (Parameter != null)
            {
                sb.Append("(");
                sb.Append(Parameter.BuildText());
                sb.Append(")");
            }
            sb.Append(" ");
            sb.Append(Body!.BuildText());
            return sb.ToString();
        }

        public override void FromString(string text, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(text, "anonymous", p => p.onRoutine(), reporter);
            var walker = new SyntaxWalker("anonymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        public SyntaxScopeType ScopeType => SyntaxScopeType.OnRoutine;

        public Dictionary<string, ISyntaxScope> SubScopes { get; private set; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous => true;
    }
}