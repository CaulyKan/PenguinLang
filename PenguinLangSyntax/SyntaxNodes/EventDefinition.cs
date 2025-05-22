using Antlr4.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PenguinLangSyntax.SyntaxNodes
{
    public class EventDefinition : SyntaxNode
    {
        public override void Build(SyntaxWalker walker, ParserRuleContext ctx)
        {
            base.Build(walker, ctx);

            if (ctx is EventDefinitionContext context)
            {
                EventIdentifier = Build<SymbolIdentifier>(walker, context.identifier());

                if (context.typeSpecifier() != null)
                {
                    EventType = Build<TypeSpecifier>(walker, context.typeSpecifier());
                }
                else
                {
                    EventType = new TypeSpecifier
                    {
                        TypeName = "void",
                        IsIterable = false
                    };
                }
            }
            else throw new NotImplementedException();
        }

        public override void FromString(string source, uint scopeDepth, ErrorReporter reporter)
        {
            var syntaxNode = PenguinParser.Parse(source, "annoymous", p => p.eventDefinition(), reporter);
            var walker = new SyntaxWalker("annoymous", reporter, scopeDepth);
            Build(walker, syntaxNode);
        }

        [ChildrenNode]
        public Identifier? EventIdentifier { get; private set; }

        [ChildrenNode]
        public TypeSpecifier? EventType { get; private set; }

        public string Name => EventIdentifier!.Name;

        public override string BuildText()
        {
            if (EventType == null || EventType.TypeName == "void")
            {
                return $"event {Name};";
            }
            return $"event {Name} : {EventType.BuildText()};";
        }
    }
}