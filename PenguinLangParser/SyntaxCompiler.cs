using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace PenguinLangParser
{
    public class SyntaxCompiler(string file, PenguinLangParser.CompilationUnitContext ast, ErrorReporter reporter)
    {
        public void Compile()
        {
            var walker = new SyntaxWalker(FileName, Reporter);
            _ = SyntaxNode.Build<NamespaceDefinition>(walker, Ast);
            Namespaces = walker.Namespaces.FindAll(x => !x.IsEmpty).ToList();
            Reporter.Write(DiagnosticLevel.Debug, $"Syntax Tree for {FileName}:\n" + GenerateAstReport());
        }
        public PenguinLangParser.CompilationUnitContext Ast { get; } = ast;
        public ErrorReporter Reporter { get; } = reporter;
        public string FileName { get; } = file;
        public List<NamespaceDefinition> Namespaces { get; private set; } = [];

        public string GenerateAstReport()
        {
            return string.Join("\n", Namespaces.SelectMany(x => (x as ISyntaxNode).PrettyPrint(0)));
        }
    }

    public class SyntaxWalker(string file, ErrorReporter reporter)
    {
        public ErrorReporter Reporter { get; } = reporter;

        public string FileName { get; } = file;

        public List<NamespaceDefinition> Namespaces { get; } = [];

        public ISyntaxScope? CurrentScope => ScopeStack.Count > 0 ? ScopeStack.Peek() : null;

        Stack<ISyntaxScope> ScopeStack { get; } = [];

        private static uint scopeIdCounter = 0;

        /// <summary>
        /// Maps each ScopeId to its parent ScopeId (0 means no parent / root).
        /// Used to walk the scope chain for variable visibility checks.
        /// Thread-safe because tests run in parallel.
        /// </summary>
        public static ConcurrentDictionary<uint, uint> ScopeParentMap { get; } = [];

        /// <summary>
        /// Unique scope ID for the innermost scope. ScopeId is monotonically increasing,
        /// so each scope gets a unique ID. This allows distinguishing sibling scopes.
        /// </summary>
        public uint CurrentScopeId { get; private set; } = 0;

        public void PopScope()
        {
            ScopeStack.Pop();
            CurrentScopeId = ScopeStack.Count > 0 ? ScopeStack.Peek().ScopeId : 0;
        }

        public void PushScope(SyntaxScopeType type, ISyntaxScope scope)
        {
            scope.ParentScope = ScopeStack.Count > 0 ? CurrentScope : null;
            scope.ScopeId = (uint)Interlocked.Increment(ref scopeIdCounter);
            CurrentScopeId = scope.ScopeId;

            // Record parent-child relationship for scope chain walking
            var parentScopeId = ScopeStack.Count > 0 ? CurrentScope!.ScopeId : 0;
            ScopeParentMap[scope.ScopeId] = parentScopeId;

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
        /// Unique scope identifier. Monotonically increasing
        /// so each scope gets a unique ID, allowing sibling scopes to be distinguished.
        /// </summary>
        uint ScopeId { get; set; }

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