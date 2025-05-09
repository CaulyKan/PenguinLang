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
            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Syntax Tree for {FileName}:\n" + string.Join("\n", Namespaces.SelectMany(x => x.PrettyPrint(0))));
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

        public void PopScope()
        {
            ScopeStack.Pop();
        }

        public void PushScope(SyntaxScopeType type, ISyntaxScope scope)
        {
            scope.ParentScope = ScopeStack.Count > 0 ? CurrentScope : null;
            scope.ScopeDepth = (scope.ParentScope?.ScopeDepth ?? InitialScopeDepth) + 1;

            switch (type)
            {
                case SyntaxScopeType.Namespace:
                    {
                        var ns = scope as NamespaceDefinition ?? throw new NotImplementedException();
                        Namespaces.Add(ns);
                        break;
                    }

                case SyntaxScopeType.Class:
                    {
                        var _ = scope as ClassDefinition ?? throw new NotImplementedException();
                        break;
                    }

                case SyntaxScopeType.Enum:
                    {
                        var _ = scope as EnumDefinition ?? throw new NotImplementedException();
                        break;
                    }

                case SyntaxScopeType.Function:
                    {
                        var _ = scope as FunctionDefinition ?? throw new NotImplementedException();
                        break;
                    }

                case SyntaxScopeType.InitialRoutine:
                    {
                        var _ = scope as InitialRoutineDefinition ?? throw new NotImplementedException();
                        break;
                    }

                case SyntaxScopeType.CodeBlock:
                    break;
            }

            ScopeStack.Push(scope);
        }

        public void DefineSymbol(string name, string type, SyntaxNode symbol)
        {
            CurrentScope!.Symbols.Add(new SyntaxSymbol(name, type, symbol));
        }
    }

    public interface ISyntaxExpression
    {
        /// <summary>
        /// if expression is a constant, symbol, or literal
        /// </summary>
        bool IsSimple { get; }

        SourceLocation SourceLocation { get; }

        T CreateWrapperExpression<T>() where T : class, ISyntaxExpression
        {
            var exp = this;
            while (exp is not T)
            {
                exp = exp.CreateWrapperExpression();
            }
            return (T)exp;
        }

        ISyntaxExpression CreateWrapperExpression();

        ISyntaxExpression GetEffectiveExpression();
    }

    public enum SyntaxScopeType
    {
        Namespace,
        Class,
        Function,
        InitialRoutine,
        CodeBlock,
        Enum,
        Interface,
        InterfaceImplementation,
    }

    public interface ISyntaxScope
    {
        public string Name { get; }

        SyntaxScopeType ScopeType { get; }

        List<SyntaxSymbol> Symbols { get; }

        Dictionary<string, ISyntaxScope> SubScopes { get; }

        bool IsAnonymous { get; }

        /// <summary>
        /// Scope depth of the symbol, each '{}' block increases the depth by 1.
        /// </summary>
        uint ScopeDepth { get; set; }

        ISyntaxScope? ParentScope { get; set; }
    }

    public class SyntaxSymbol(string name, string typeName, SyntaxNode symbol)
    {
        public string Name { get; } = name;
        public string TypeName { get; } = typeName;
        public SyntaxNode SyntaxNode { get; } = symbol;
    }
}