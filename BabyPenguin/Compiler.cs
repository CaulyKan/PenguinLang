using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using PenguinLangAntlr;

namespace BabyPenguin
{
    public class Compiler : ICompilerInfo
    {
        public Compiler(string file, PenguinLangParser.CompilationUnitContext ast, ErrorReporter reporter)
        {
            Ast = ast;
            Reporter = reporter;
            FileName = file;
        }

        public void Compile()
        {
            new Namespace(this, Ast);
        }

        public string PrintSemanticTree()
        {
            return string.Join("\n", Namespaces.SelectMany(x => x.PrettyPrint(0)));
        }

        public PenguinLangParser.CompilationUnitContext Ast { get; }
        public ErrorReporter Reporter { get; }
        public string FileName { get; set; }

        List<Namespace> Namespaces { get; } = [];

        IScope CurrentScope => ScopeStack.Peek();
        Stack<IScope> ScopeStack { get; } = [];

        public void PopScope()
        {
            ScopeStack.Pop();
        }

        public void PushScope(ScopeType type, IScope scope)
        {
            scope.ParentScope = ScopeStack.Count > 0 ? CurrentScope : null;

            switch (type)
            {
                case ScopeType.Namespace:
                    {
                        var ns = scope as Namespace ?? throw new ArgumentException("Scope must be of type Namespace");
                        Namespaces.Add(ns);
                        break;
                    }

                case ScopeType.Class:
                    throw new NotImplementedException();
                case ScopeType.Function:
                    {
                        var _ = scope as FunctionDefinition ?? throw new ArgumentException("Scope must be of type FunctionDefinition");
                        break;
                    }

                case ScopeType.InitialRoutine:
                    {
                        var _ = scope as InitialRoutine ?? throw new ArgumentException("Scope must be of type InitialRoutine");
                        break;
                    }

                case ScopeType.CodeBlock:
                    break;
            }

            ScopeStack.Push(scope);
        }
    }

    public interface ICompilerInfo
    {
        ErrorReporter Reporter { get; }
        string FileName { get; }
        void PushScope(ScopeType type, IScope scope);
        void PopScope();
    }

    public interface IExpression
    {
        /// <summary>
        /// if expression is a constant, symbol, or literal
        /// </summary>
        bool IsSimple { get; }
    }

    public enum ScopeType
    {
        Namespace,
        Class,
        Function,
        InitialRoutine,
        CodeBlock,
    }

    public interface IScope
    {
        public string Id { get; }

        public string GetFullScopeId() => ParentScope == null ? Id : $"{ParentScope.GetFullScopeId()}.{Id}";

        ScopeType ScopeType { get; }

        List<Symbol> Symbols { get; }

        Dictionary<string, IScope> SubScopes { get; }

        IScope? ParentScope { get; set; }
    }

    public class Symbol(string name, string full_name, bool is_local, TypeSpecifierEnum type_specifier, string type_name)
    {
        public string Name { get; } = name;
        public string FullName { get; } = full_name;
        public bool IsLocal { get; } = is_local;
        public TypeSpecifierEnum TypeSpecifier { get; } = type_specifier;
        public string TypeName { get; } = type_name;
    }
}