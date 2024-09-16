using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using PenguinLangAntlr;

namespace BabyPenguin
{
    using Syntax;

    public class SyntaxCompiler(string file, PenguinLangParser.CompilationUnitContext ast, ErrorReporter reporter)
    {
        public void Compile()
        {
            var walker = new SyntaxWalker(FileName, Reporter);
            _ = new Namespace(walker, Ast);
            Namespaces = walker.Namespaces;
        }

        public string PrintSyntaxTree()
        {
            return string.Join("\n", Namespaces.SelectMany(x => x.PrettyPrint(0)));
        }

        public PenguinLangParser.CompilationUnitContext Ast { get; } = ast;
        public ErrorReporter Reporter { get; } = reporter;
        public string FileName { get; } = file;
        public List<Namespace> Namespaces { get; private set; } = [];
    }


    public class SyntaxWalker(string file, ErrorReporter reporter)
    {
        public ErrorReporter Reporter { get; } = reporter;
        public string FileName { get; } = file;

        public List<Namespace> Namespaces { get; } = [];

        public ISyntaxScope? CurrentScope => ScopeStack.Count > 0 ? ScopeStack.Peek() : null;
        Stack<ISyntaxScope> ScopeStack { get; } = [];

        public void PopScope()
        {
            ScopeStack.Pop();
        }

        public void PushScope(SyntaxScopeType type, ISyntaxScope scope)
        {
            scope.ParentScope = ScopeStack.Count > 0 ? CurrentScope : null;

            switch (type)
            {
                case SyntaxScopeType.Namespace:
                    {
                        var ns = scope as Namespace ?? throw new ArgumentException("Scope must be of type Namespace");
                        Namespaces.Add(ns);
                        break;
                    }

                case SyntaxScopeType.Class:
                    {
                        var _ = scope as ClassDefinition ?? throw new ArgumentException("Scope must be of type ClassDefinition");
                        break;
                    }
                case SyntaxScopeType.Function:
                    {
                        var _ = scope as FunctionDefinition ?? throw new ArgumentException("Scope must be of type FunctionDefinition");
                        break;
                    }

                case SyntaxScopeType.InitialRoutine:
                    {
                        var _ = scope as InitialRoutine ?? throw new ArgumentException("Scope must be of type InitialRoutine");
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
    }

    public enum SyntaxScopeType
    {
        Namespace,
        Class,
        Function,
        InitialRoutine,
        CodeBlock,
    }

    public interface ISyntaxScope
    {
        public string Name { get; }

        public string GetScopeName()
        {
            if (ParentScope == null)
            {
                return Name;
            }
            else if
                (ParentScope.ScopeType == SyntaxScopeType.Namespace &&
                    ParentScope.IsAnonymous && this.ScopeType == SyntaxScopeType.Namespace)
            {
                return Name;
            }
            else
            {
                return ParentScope.GetScopeName() + "." + Name;
            }
        }

        SyntaxScopeType ScopeType { get; }

        List<SyntaxSymbol> Symbols { get; }

        Dictionary<string, ISyntaxScope> SubScopes { get; }

        bool IsAnonymous { get; }

        ISyntaxScope? ParentScope { get; set; }
    }

    public class SyntaxSymbol(string name, string typeName, SyntaxNode symbol)
    {
        public string Name { get; } = name;
        public string TypeName { get; } = typeName;
        public SyntaxNode SyntaxNode { get; } = symbol;
    }
}