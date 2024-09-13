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
            return string.Join("\n", Namespaces.Values.SelectMany(x => x.PrettyPrint(0)));
        }

        public PenguinLangParser.CompilationUnitContext Ast { get; }
        public ErrorReporter Reporter { get; }
        public string FileName { get; set; }
        public Stack<Namespace> NamespaceStack { get; } = new Stack<Namespace>();
        public Dictionary<string, Namespace> Namespaces { get; } = new Dictionary<string, Namespace>();
        public IRoutine? CurrentRoutine { get; set; } = null;
        public List<IRoutine> Routines { get; } = new();
    }

    public class Symbol
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public bool IsLocal { get; set; }
        public TypeSpecifierEnum TypeSpecifier { get; set; }
        public string FullTypeName { get; set; }
    }
}