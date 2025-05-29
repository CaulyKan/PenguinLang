using System.Collections.Generic;
using System;
using System.Linq;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace PenguinLangSyntax
{
    public class SyntaxCompiler(string file, PenguinLangParser.CompilationUnitContext ast, ErrorReporter reporter)
    {
        public void Compile()
        {
            var walker = new SyntaxWalker(FileName, Reporter);
            _ = SyntaxNode.Build<NamespaceDefinition>(walker, Ast);
            Namespaces = walker.Namespaces.FindAll(x => !x.IsEmpty).ToList();
            Reporter.Write(DiagnosticLevel.Debug, $"Syntax Tree for {FileName}:\n" + string.Join("\n", Namespaces.SelectMany(x => (x as ISyntaxNode).PrettyPrint(0))));
        }
        public PenguinLangParser.CompilationUnitContext Ast { get; } = ast;
        public ErrorReporter Reporter { get; } = reporter;
        public string FileName { get; } = file;
        public List<NamespaceDefinition> Namespaces { get; private set; } = [];
    }

    public class SyntaxWalker(string file, ErrorReporter reporter, uint scopeDepth = 0)
    {
        public ErrorReporter Reporter { get; } = reporter;

        public string FileName { get; } = file;

        public List<NamespaceDefinition> Namespaces { get; } = [];

        public ISyntaxScope? CurrentScope => ScopeStack.Count > 0 ? ScopeStack.Peek() : null;

        Stack<ISyntaxScope> ScopeStack { get; } = [];

        public uint InitialScopeDepth { get; } = scopeDepth;

        public uint CurrentScopeDepth { get; private set; } = scopeDepth;

        public void PopScope()
        {
            ScopeStack.Pop();
            CurrentScopeDepth -= 1;
        }

        public void PushScope(SyntaxScopeType type, ISyntaxScope scope)
        {
            scope.ParentScope = ScopeStack.Count > 0 ? CurrentScope : null;
            scope.ScopeDepth = CurrentScopeDepth;
            CurrentScopeDepth += 1;

            if (type == SyntaxScopeType.Namespace)
                Namespaces.Add(scope as NamespaceDefinition ?? throw new NotImplementedException());


            ScopeStack.Push(scope);
        }
    }

    public enum SyntaxScopeType
    {
        Namespace,
        Class,
        Function,
        LambdaFunction,
        InitialRoutine,
        OnRoutine,
        CodeBlock,
        Enum,
        Interface,
        InterfaceImplementation,
    }

    public interface ISyntaxScope
    {
        public string Name { get; }

        SyntaxScopeType ScopeType { get; }

        Dictionary<string, ISyntaxScope> SubScopes { get; }

        bool IsAnonymous { get; }

        /// <summary>
        /// Scope depth of the symbol, each '{}' block increases the depth by 1.
        /// </summary>
        uint ScopeDepth { get; set; }

        ISyntaxScope? ParentScope { get; set; }

        SourceLocation SourceLocation { get; }
    }

    public class SyntaxSymbol(string name, string typeName, SyntaxNode symbol)
    {
        public string Name { get; } = name;
        public string TypeName { get; } = typeName;
        public SyntaxNode SyntaxNode { get; } = symbol;
    }
}